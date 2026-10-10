using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Headless
{
    // Trusted evaluator only. This never feeds an observation, policy, command or save.
    public sealed class HeadlessEconomyMetrics
    {
        public sealed class Settlement
        {
            public long Tick{get;set;} public int SiteId{get;set;} public double Amount{get;set;}
            public double OwnerSettledDelta{get;set;}
        }
        public sealed class FactoryReport
        {
            public int Id{get;set;} public int SiteId{get;set;} public int ParentId{get;set;} public string Phase{get;set;}
            public int QueueCount{get;set;} public long HeadId{get;set;} public int HeadPaidCost{get;set;} public double HeadRemaining{get;set;} public bool HeadActive{get;set;}
            public bool TankExitUsable{get;set;}
        }
        public sealed class OwnerReport
        {
            public FactoryReport[] LatestFactories{get;set;}=Array.Empty<FactoryReport>();
            public string OwnerId{get;set;} public long SampledTicks{get;set;}
            public long ExcessBankTicks{get;set;} public long ExcessBankLongestRun{get;set;} public long ExcessBankOverDeadlineTicks{get;set;}
            public long IdleAffordableLineTicks{get;set;} public long IdleAffordableLongestRun{get;set;} public long IdleAffordableOverDeadlineLineTicks{get;set;}
            public long ExcessBankDeadlineTicks{get;set;} public long IdleLineDeadlineTicks{get;set;}
            public long BlockedExitLineTicks{get;set;} public long LowBankTicks{get;set;} public long FullLocalSlotsTicks{get;set;}
            public long PopulationBlockedTicks{get;set;} public long LostHeadquartersTicks{get;set;}
            public double RealizedExpansionIncome{get;set;} public double SettledIncome{get;set;}
            public int LatestBank{get;set;} public long LatestUnpaid{get;set;} public long LatestReserved{get;set;} public long LatestSpendableBank{get;set;}
            public int ActiveMajorArmies{get;set;} public int PeakMajorArmies{get;set;} public long MajorArmyTicks{get;set;}
            public long ReassignedMembers{get;set;} public long ChangedArmyObjectives{get;set;} public long OverdueObjectiveTicks{get;set;}
            public long AppliedCommands{get;set;} public long AppliedTacticalCommands{get;set;}
            public double CommandsPerSimulationSecond=>SampledTicks==0?0:AppliedCommands*30d/SampledTicks;
            public double TacticalCommandsPerSimulationSecond=>SampledTicks==0?0:AppliedTacticalCommands*30d/SampledTicks;
            public int PeakBank{get;set;} public long PeakSpendableBank{get;set;} public long PeakUnpaid{get;set;} public long PeakReserved{get;set;}
            public int AdditionalFactories{get;set;} public int ProducedUnits{get;set;}
            public List<Settlement> ExpansionSettlementExamples{get;}=new List<Settlement>();
        }
        private sealed class State
        {
            internal OwnerReport Report; internal HashSet<int> Centers,Factories,Units; internal double Income;
            internal long Tick,ExcessRun; internal readonly Dictionary<int,long> IdleRuns=new Dictionary<int,long>();
            internal readonly Dictionary<int,long> Membership=new Dictionary<int,long>();
            internal readonly Dictionary<long,string> Objectives=new Dictionary<long,string>();
            internal long LastAppliedAction;
            internal readonly AiProductionDemand Demand=new AiProductionDemand();
        }
        private readonly SortedDictionary<string,State> owners=new SortedDictionary<string,State>(StringComparer.Ordinal);
        private readonly AiProfile ai;
        public HeadlessEconomyMetrics(PlayableAuthorityTick authority,AiProfile profile)
        {
            ai=profile;
            foreach(var ledger in authority.CaptureDiagnosticBudgets())
            {
                var v=authority.ParticipantView(ledger.OwnerId);
                owners.Add(ledger.OwnerId,new State{Report=new OwnerReport{OwnerId=ledger.OwnerId,ExcessBankDeadlineTicks=AiProfile.SecondsToTicks(ai.Value("economy.excessBankDeadlineSeconds"),30),IdleLineDeadlineTicks=AiProfile.SecondsToTicks(ai.Value("economy.idleLineDeadlineSeconds"),30)},Income=v.SettledIncome,Tick=v.Tick,
                    Centers=new HashSet<int>(v.Buildings.Where(b=>b.Owner==v.Owner&&TerritoryRules.Center(b.Kind)).Select(b=>b.SiteId)),Factories=new HashSet<int>(v.Buildings.Where(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Factory).Select(b=>b.Id)),Units=new HashSet<int>(v.Entities.Where(e=>e.Owner==v.Owner).Select(e=>e.Id))});
            }
        }
        public OwnerReport[] Reports=>owners.Values.Select(s=>s.Report).ToArray();
        public void Observe(PlayableAuthorityTick authority)
        {
            foreach(var ledger in authority.CaptureDiagnosticBudgets())
            {
                var s=owners[ledger.OwnerId];var v=authority.ParticipantView(ledger.OwnerId);long dt=v.Tick-s.Tick;
                if(dt==0)continue;
                if(dt!=1||v.SettledIncome<s.Income)throw new InvalidOperationException("Macro diagnostics require consecutive authority settlements.");
                var r=s.Report;var p=v.ActiveProfile;var o=PlayableAiObservation.From(v);
                // Reuse the existing trusted evaluator/checkpoint stream. These diagnostics never
                // feed planning or saves, and pause (dt==0 above) contributes no samples.
                var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId==ledger.OwnerId);
                var active=checkpoint.Armies.Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded).ToArray();
                r.ActiveMajorArmies=active.Count(a=>a.Major);r.PeakMajorArmies=Math.Max(r.PeakMajorArmies,r.ActiveMajorArmies);r.MajorArmyTicks+=r.ActiveMajorArmies;
                foreach(var a in active)
                {
                    foreach(var id in a.Members)
                    {if(s.Membership.TryGetValue(id,out var previous)&&previous!=a.Id)r.ReassignedMembers++;s.Membership[id]=a.Id;}
                    if(s.Objectives.TryGetValue(a.Id,out var objective)&&objective!=a.Objective)r.ChangedArmyObjectives++;
                    s.Objectives[a.Id]=a.Objective;
                    if(a.Major&&!string.IsNullOrEmpty(a.Objective)&&v.Tick-a.ProgressTick>AiProfile.SecondsToTicks(ai.Value("armies.offensiveOverdueSeconds"),30))r.OverdueObjectiveTicks++;
                }
                var applied=checkpoint.Records.Where(c=>c.Status==PlayableAiDeliveryStatus.Applied&&c.ActionId>s.LastAppliedAction).ToArray();
                r.AppliedCommands+=applied.Length;
                r.AppliedTacticalCommands+=applied.Count(c=>c.Kind==PlayableCommandKind.Move||c.Kind==PlayableCommandKind.Attack||c.Kind==PlayableCommandKind.AttackMove||c.Kind==PlayableCommandKind.Follow||c.Kind==PlayableCommandKind.Hold||c.Kind==PlayableCommandKind.Stop);
                if(applied.Length>0)s.LastAppliedAction=applied.Max(c=>c.ActionId);
                long unpaid=ledger.Unpaid.Sum(e=>(long)e.Amount),reserved=ledger.Reserved.Sum(e=>(long)e.Amount);
                long spendable=Math.Max(0,(long)v.Credits-ledger.SafetyReserve-unpaid-reserved);
                s.Demand.Observe(o,ai);var plan=new AiEconomyPlanner().Plan(o,p,s.Demand,ai,spendable);
                bool excess=plan.Candidates.Any(c=>c.Legal&&c.Action.Kind==PlayableCommandKind.BuildAt&&TerritoryRules.Cost(p,c.Action.BuildingKind)<=spendable);
                bool affordableIdle=false;var idleIds=new HashSet<int>();
                foreach(var b in v.Buildings.Where(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState!=null&&b.PrivateState.Lifecycle?.Selling!=true))
                {
                    if(b.PrivateState.Orders.Any(q=>q.Active&&q.Remaining<=0)){r.BlockedExitLineTicks++;continue;}
                    if(b.PrivateState.QueueCount!=0)continue;
                    bool legal=false;
                    foreach(var kind in new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval})
                    {
                        var command=kind==PlayableEntityKind.Tank?PlayableCommandKind.QueueTank:kind==PlayableEntityKind.Explorer?PlayableCommandKind.QueueExplorer:PlayableCommandKind.QueueShkval;
                        var a=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,command,new[]{b.Id},unitKind:kind);
                        if(PlayableUnitRules.Cost(p,kind)<=spendable&&AiEconomyAdmission.Reject(o,p,a,true)==null&&authority.DiagnosticFactoryExitUsable(b.Id,kind)){legal=true;break;}
                    }
                    if(!legal)continue;
                    affordableIdle=true;idleIds.Add(b.Id);long run=s.IdleRuns.TryGetValue(b.Id,out var previous)?previous+1:1;s.IdleRuns[b.Id]=run;
                    r.IdleAffordableLineTicks++;r.IdleAffordableLongestRun=Math.Max(r.IdleAffordableLongestRun,run);if(run>r.IdleLineDeadlineTicks)r.IdleAffordableOverDeadlineLineTicks++;
                }
                foreach(var id in s.IdleRuns.Keys.Where(id=>!idleIds.Contains(id)).ToArray())s.IdleRuns.Remove(id);
                excess|=affordableIdle;
                s.ExcessRun=excess?s.ExcessRun+1:0;if(excess)r.ExcessBankTicks++;r.ExcessBankLongestRun=Math.Max(r.ExcessBankLongestRun,s.ExcessRun);if(s.ExcessRun>r.ExcessBankDeadlineTicks)r.ExcessBankOverDeadlineTicks++;
                if(spendable<new[]{p.TankCreditCost,p.ExplorerCreditCost,p.ShkvalCreditCost}.Min())r.LowBankTicks++;
                if(o.Population!=null&&o.Population.Capacity-o.Population.Living-o.Population.Reserved<PlayableUnitRules.Population(p,PlayableEntityKind.Tank))r.PopulationBlockedTicks++;
                var local=v.Sites.FirstOrDefault(site=>site.Site.Id==v.HomeSiteId);
                if(local!=null&&local.Site.Slots.Count>0&&local.Site.Slots.All(slot=>v.Buildings.Any(b=>b.SiteId==local.Site.Id&&b.SlotId==slot.Id)))r.FullLocalSlotsTicks++;
                if(!v.Buildings.Any(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Headquarters&&b.Health>0))r.LostHeadquartersTicks++;
                // World settles once per second, AFTER construction/lifecycle/upgrades. Attribute
                // only an observed positive settlement, to the actual ready income sources.
                double delta=v.SettledIncome-s.Income;
                if(delta>0)
                {
                    var ready=v.Buildings.Where(b=>b.Owner==v.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true).ToArray();
                    double Rate(PlayableBuildingSnapshot b)=>(b.RefineryUpgraded?p.RefineryUpgradedIncome:TerritoryRules.Income(p,b.Kind))/p.IncomePeriodSeconds;
                    double total=ready.Sum(Rate);
                    if(Math.Abs(delta-total)>1e-7)throw new InvalidOperationException("Actual settled income/source attribution mismatch.");
                    foreach(var site in ready.Where(b=>!s.Centers.Contains(b.SiteId)).GroupBy(b=>b.SiteId))
                    {
                        double amount=site.Sum(Rate);r.RealizedExpansionIncome+=amount;
                        if(amount>0&&r.ExpansionSettlementExamples.Count<32)r.ExpansionSettlementExamples.Add(new Settlement{Tick=v.Tick,SiteId=site.Key,Amount=amount,OwnerSettledDelta=delta});
                    }
                }
                r.LatestFactories=v.Buildings.Where(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Factory).OrderBy(b=>b.Id).Take(64).Select(b=>{var head=b.PrivateState?.Orders.FirstOrDefault();return new FactoryReport{Id=b.Id,SiteId=b.SiteId,ParentId=b.ParentId,Phase=b.Phase.ToString(),QueueCount=b.PrivateState?.QueueCount??0,HeadId=head?.Id??0,HeadPaidCost=head?.PaidCost??0,HeadRemaining=head?.Remaining??0,HeadActive=head?.Active??false,TankExitUsable=authority.DiagnosticFactoryExitUsable(b.Id,PlayableEntityKind.Tank)};}).ToArray();
                r.LatestBank=v.Credits;r.LatestUnpaid=unpaid;r.LatestReserved=reserved;r.LatestSpendableBank=spendable;
                r.SampledTicks++;r.SettledIncome=v.SettledIncome;r.PeakBank=Math.Max(r.PeakBank,v.Credits);r.PeakSpendableBank=Math.Max(r.PeakSpendableBank,spendable);r.PeakUnpaid=Math.Max(r.PeakUnpaid,unpaid);r.PeakReserved=Math.Max(r.PeakReserved,reserved);
                r.AdditionalFactories=Math.Max(r.AdditionalFactories,v.Buildings.Count(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Factory&&!s.Factories.Contains(b.Id)));
                r.ProducedUnits=Math.Max(r.ProducedUnits,v.Entities.Count(e=>e.Owner==v.Owner&&!s.Units.Contains(e.Id)));
                s.Income=v.SettledIncome;s.Tick=v.Tick;
            }
        }
    }
}

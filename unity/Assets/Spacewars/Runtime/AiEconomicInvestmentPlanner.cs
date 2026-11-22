using System;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // A current-world quote, not another promise of money. The ordinary ledger owns
    // accepted work; the quote only withholds new discretionary purchases this review.
    public sealed class AiEconomicInvestment
    {
        internal AiEconomicInvestment(AiEconomyCandidate candidate,int credits,string reason,int additionalProductionProtection)
        {Candidate=candidate;Credits=credits;Reason=reason;AdditionalProductionProtection=additionalProductionProtection;}
        public AiEconomyCandidate Candidate{get;}
        public int Credits{get;}
        public string Reason{get;}
        public int AdditionalProductionProtection{get;}
        public bool AllowsProduction(long available,int cost,bool acceptedStartup=false,bool maySpendProtectedProductionQuote=false)
            =>acceptedStartup||cost<=Math.Max(0,available-Credits-(maySpendProtectedProductionQuote?0:AdditionalProductionProtection));
    }
    public static class AiEconomicInvestmentPlanner
    {
        private static bool Producer(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Factory||kind==PlayableBuildingKind.Hangar;
        private static bool IncomeBuilding(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Refinery||kind==PlayableBuildingKind.Mine;
        private static bool Live(PlayableBuildingSnapshot b)=>b.PrivateState?.Lifecycle?.Selling!=true&&
            (b.Health>0||b.Phase==ConstructionPhase.Pending||b.Phase==ConstructionPhase.Constructing&&b.Progress==0);
        internal static int ReadyIncomeCount(PlayableAiObservation o,int excluding=0)=>o.Buildings.Count(b=>b.Owner==o.Owner&&
            b.Id!=excluding&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&Live(b)&&IncomeBuilding(b.Kind));
        internal static bool MixAction(PlayableBuildingKind kind)=>Producer(kind)||kind==PlayableBuildingKind.ScientificCenter;
        internal static string ScienceIncomeRejection(PlayableAiObservation o,PlayableProfile p,AiProfile ai,int excluding=0)
        {
            if(AiProfile.UsesExpertPolicy(o.Difficulty))
                return ExpertCoversConstructionSpend(o,p,ai,PlayableEntityKind.Tank,0)?null:"current income below useful queue spend";
            return ReadyIncomeCount(o,excluding)<ai.Value("technology.minimumIncomeBuildings")?"ready refinery or mine prerequisite":null;
        }
        internal static string MixRejection(PlayableAiObservation o,PlayableProfile p,AiProfile ai,PlayableBuildingKind kind,int siteId,
            int excluding=0,PlayableEntityKind groundKind=PlayableEntityKind.Tank)
        {
            if(!MixAction(kind))return null;
            bool science=kind==PlayableBuildingKind.ScientificCenter;
            if(science)
            {
                var rejection=ScienceIncomeRejection(o,p,ai,excluding);
                if(rejection!=null)return rejection;
            }
            var local=o.Buildings.Where(b=>b.Owner==o.Owner&&b.SiteId==siteId&&Live(b)).ToArray();
            int producers=local.Count(b=>Producer(b.Kind))+(science?0:1);
            bool mixed=producers>=2&&(science||local.Any(b=>b.Kind==PlayableBuildingKind.ScientificCenter));
            if(!mixed)return null;
            if(AiProfile.UsesExpertPolicy(o.Difficulty))
            {
                if(science)return null; // The same queue budget already admitted the center.
                var addedKind=kind==PlayableBuildingKind.Hangar?PlayableEntityKind.Hawk:groundKind;
                return ExpertCoversConstructionSpend(o,p,ai,groundKind,UnitSpend(p,addedKind))?
                    null:"current income below projected production spend";
            }
            double minimum=Math.Max(ai.Value("technology.minimumIncomeBuildings"),ai.Value("technology.mixedBaseIncomeBuildings"));
            return ReadyIncomeCount(o,excluding)<minimum?"ready income required for two producers and science":null;
        }

        private static bool ExpertCoversConstructionSpend(PlayableAiObservation o,PlayableProfile p,AiProfile ai,PlayableEntityKind groundKind,double addedSpend)
            =>o.IncomePerSecond>0&&CoversSpend(o.IncomePerSecond,QueueSpend(o,p,groundKind)+addedSpend,ai);
        internal static double UnitSpend(PlayableProfile p,PlayableEntityKind kind)=>
            PlayableUnitRules.Cost(p,kind)/PlayableUnitRules.Duration(p,kind);
        internal static double QueueSpend(PlayableAiObservation o,PlayableProfile p,PlayableEntityKind groundKind)
        {
            int free=o.Population==null?0:o.Population.Capacity-o.Population.Living-o.Population.Reserved;
            double spend=0;
            foreach(var b in o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&
                Live(b)&&!b.SystemsDisabled&&b.PrivateState!=null))
            {
                if(Producer(b.Kind))
                {
                    var head=b.PrivateState.Orders.FirstOrDefault();
                    var kind=b.Kind==PlayableBuildingKind.Hangar?PlayableEntityKind.Hawk:groundKind;
                    if(head!=null&&!head.IsUpgrade&&head.Duration>0&&AiProductionDemand.Productive(b))
                        spend+=head.PaidCost/head.Duration;
                    else if(head==null&&free>=PlayableUnitRules.Population(p,kind))
                        spend+=UnitSpend(p,kind)*(b.ProducerUpgraded?p.UpgradedProducerSpeedMultiplier:1);
                }
                if(b.Kind==PlayableBuildingKind.ScientificCenter)
                    spend+=o.OwnerResearch.Where(r=>r.CenterId==b.Id&&r.Active&&!r.Complete&&r.Duration>0).Sum(r=>r.PaidCost/r.Duration);
            }
            return spend;
        }
        internal static double SustainableIncome(PlayableAiObservation observation,PlayableProfile profile,AiProfile ai)
        {
            double income=observation.IncomePerSecond;
            double seconds=ai.Value("economy.incomeWindowSeconds");
            foreach(var site in observation.Sites)
            {
                if(site.Owner!=observation.Owner||!site.Ready||!site.MineReserve.HasValue||site.MineReserve.Value<=0)continue;
                var mine=observation.Buildings.FirstOrDefault(b=>b.Id==site.CenterId);
                if(mine==null||mine.SystemsDisabled||mine.PrivateState?.Lifecycle?.Selling==true)continue;
                income+=(TerritoryRules.MineIncome(profile,site.MineReserve,seconds)-TerritoryRules.MineIncome(profile,null,seconds))/seconds;
            }
            return Math.Max(0,income);
        }

        internal static bool CoversSpend(double income,double spend,AiProfile ai)=>income>=spend*ai.Value("economy.minimumIncomeToSpendRatio");
        internal static bool HasSurplus(double income,double spend,AiProfile ai)=>income>spend*ai.Value("economy.surplusIncomeToSpendRatio");
        internal static PlayableAiAction BuildingUpgrade(PlayableAiObservation o,PlayableProfile p,AiProfile ai,
            PlayableEntityKind groundKind,long freeCredits,bool ordinaryResearchPending,bool prioritizeAirDefense=false)
        {
            if(ReadyIncomeCount(o)<ai.Value("technology.minimumIncomeBuildings")||!CoversSpend(SustainableIncome(o,p,ai),QueueSpend(o,p,groundKind),ai))return null;
            var ready=o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&
                Live(b)&&!b.SystemsDisabled&&b.PrivateState!=null).OrderBy(b=>b.Id).ToArray();
            var centers=ready.Where(b=>b.Kind==PlayableBuildingKind.ScientificCenter).ToArray();
            if(!ready.Any(b=>Producer(b.Kind)))return null;
            var research=PlayableResearchKind.ScientificCenterUpgrade;
            PlayableAiAction Action(PlayableBuildingSnapshot b,PlayableCommandKind kind)=>
                new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,kind,new[]{b.Id},
                    buildingKind:b.Kind,researchKind:research,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            if(!centers.Any(b=>b.ScientificCenterUpgraded))
            {
                if(ordinaryResearchPending&&!prioritizeAirDefense||freeCredits<PlayableResearchRules.Cost(p,research)||
                    !o.ResearchAvailability.Any(r=>r.Kind==research&&r.Available))return null;
                var center=centers.FirstOrDefault(b=>!o.OwnerResearch.Any(r=>r.CenterId==b.Id&&(!r.Complete||r.Kind==research)));
                return center==null?null:Action(center,PlayableCommandKind.QueueResearch);
            }
            foreach(var b in ready.Where(b=>Producer(b.Kind)&&freeCredits>=p.ProducerUpgradeCost(b.Kind)))
            {
                var a=Action(b,PlayableCommandKind.UpgradeProducer);
                if(AiEconomyAdmission.Reject(o,p,a)==null)return a;
            }
            return null;
        }
        internal const string DemandPurpose="factory-investment-demand";
        internal static bool IsDemand(AiBudgetEntry e)=>e.Purpose==DemandPurpose;
        internal static AiBudgetEntry Demand(AiBudgetLedger budget)=>budget.Capture().Reserved.SingleOrDefault(IsDemand);
        internal static void RememberDemand(PlayableAiObservation o,PlayableProfile p,AiBudgetLedger budget,AiEconomicInvestment investment)
        {
            var old=Demand(budget);
            var action=investment?.Candidate.Action;
            bool factory=action!=null&&action.BuildingKind==PlayableBuildingKind.Factory&&investment.Credits>0;
            if(old!=null&&factory&&old.Action.SiteId==action.SiteId&&old.Action.SlotId==action.SlotId&&old.Terms==p.ProfileId+"@"+p.Revision)return;
            if(old!=null)budget.Release(old.Id);
            if(!factory)return;
            // This zero-credit witness records already qualified demand, not another
            // allocation. Its existing bounded ledger clock never renews while saving.
            var entry=new AiBudgetEntry(DemandPurpose+":"+action.SiteId+":"+action.SlotId,DemandPurpose,action,0,0,o.Tick,checked(o.Tick+budget.ReservationLifetimeTicks),
                "qualified growth demand / target loss / expiry",Array.Empty<string>(),p.ProfileId+"@"+p.Revision);
            if(!budget.Reserve(entry))throw new InvalidOperationException("Demand witness cannot reserve money.");
        }
        internal static void ValidateDemand(AiBudgetLedger budget,PlayableProfile p,long tick)
        {
            var entries=budget.Capture().Reserved.Where(IsDemand).ToArray();
            AiStateWire.Require(entries.Length<=1,"one factory demand witness");
            foreach(var e in entries)AiStateWire.Require(e.Amount==0&&e.Claims.Count==0&&e.CreatedTick<=tick&&e.ExpiresTick-e.CreatedTick<=budget.ReservationLifetimeTicks&&
                e.Terms==p.ProfileId+"@"+p.Revision&&e.Action.PlayerId==budget.OwnerId&&e.Action.Generation==budget.Generation&&e.Action.Kind==PlayableCommandKind.BuildAt&&
                e.Action.BuildingKind==PlayableBuildingKind.Factory&&e.Action.SiteId>0&&e.Action.SlotId>0&&e.Action.ParentId>0&&e.Id==DemandPurpose+":"+e.Action.SiteId+":"+e.Action.SlotId,"factory demand witness binding");
        }
        internal static bool HasPendingConstruction(PlayableAiObservation o,PlayableProfile p)=>o.Buildings.Any(b=>b.Owner==o.Owner&&b.Phase!=ConstructionPhase.Ready&&
            (b.Kind==PlayableBuildingKind.Factory||TerritoryRules.Income(p,b.Kind)>0));
        internal static bool HasIncomeSlot(PlayableAiObservation o,AiProfile ai,bool competingNextSlot=false)
        {
            var sites=o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested).ToArray();
            int free=sites.Sum(s=>s.Site.Slots.Count(slot=>!o.Buildings.Any(b=>b.SiteId==s.Site.Id&&b.SlotId==slot.Id)));
            // Science and a new producer compete for the same next slot. Hold
            // capacity for one of them while income grows in the other slots.
            int science=o.Buildings.Any(b=>b.Owner==o.Owner&&b.Health>0&&b.Kind==PlayableBuildingKind.ScientificCenter)?0:(int)ai.Value("economy.reservedScienceSlots");
            int production=ProducerCount(o)>1||HasScience(o)&&ProducerCount(o)>0?0:(int)ai.Value("economy.reservedProductionSlots");
            return free>(competingNextSlot?Math.Max(science,production):science+production);
        }
        internal static int ProducerCount(PlayableAiObservation o)=>o.Buildings.Count(b=>b.Owner==o.Owner&&Live(b)&&Producer(b.Kind));
        internal static bool HasScience(PlayableAiObservation o)=>o.Buildings.Any(b=>b.Owner==o.Owner&&Live(b)&&b.Kind==PlayableBuildingKind.ScientificCenter);
        public static AiEconomicInvestment Plan(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiProductionCapacity capacity,bool homeReady,bool emergency,AiBudgetEntry demand=null,bool initialOpeningCommitmentPending=false,int additionalProductionProtection=0,long availableCredits=-1,PlayableEntityKind startupKind=PlayableEntityKind.Tank,bool? preferTwoProducers=null,bool outpostProspect=false)
        {
            if(additionalProductionProtection<0||availableCredits< -1)throw new ArgumentOutOfRangeException();
            bool qualifiedDemand=demand!=null&&IsDemand(demand)&&demand.Amount==0&&demand.CreatedTick<=o.Tick&&demand.ExpiresTick>=o.Tick&&
                demand.Terms==p.ProfileId+"@"+p.Revision&&demand.Action.PlayerId==o.OwnerId&&demand.Action.Generation==o.Generation;
            if(!homeReady||emergency||initialOpeningCommitmentPending||capacity==null||HasPendingConstruction(o,p))return null;
            double spend=QueueSpend(o,p,startupKind);
            bool populationRoom=o.Population!=null&&
                o.Population.Capacity-o.Population.Living-o.Population.Reserved>=PlayableUnitRules.Population(p,startupKind);
            bool nextLineUseful=populationRoom&&capacity.PopulationWaitingLines==0&&ProducerCount(o)>0;
            double sustainableIncome=SustainableIncome(o,p,ai);
            bool canSupportNext=CoversSpend(sustainableIncome,spend+UnitSpend(p,startupKind),ai);
            bool needsIncome=preferTwoProducers.HasValue&&ProducerCount(o)>0&&!HasScience(o)&&
                (preferTwoProducers==false||ProducerCount(o)>1)&&ScienceIncomeRejection(o,p,ai)!=null;
            bool prepareOutpost=preferTwoProducers.HasValue&&o.PublicScoutObjectives.Any(g=>g.Reachable&&g.Role==PlayablePublicScoutObjectiveRole.ExpansionOutpost)&&
                AiOpeningExecutor.ReadyHomeBuildings(o)<ai.Value("openingsMidgame.outpostHomeBuildings");
            long spendable=availableCredits<0?o.Credits:availableCredits;
            // A paid queue and the next useful cycle keep their funds. A blocked
            // egress suppresses new factories, but can coexist with safe income.
            bool measured=capacity.MeasuredTicks>=AiProfile.SecondsToTicks(ai.Value("economy.incomeWindowSeconds"),30);
            bool spareIncome=(preferTwoProducers.HasValue||HasScience(o))&&!qualifiedDemand&&measured&&HasSurplus(sustainableIncome,spend,ai)&&
                !capacity.CanScale&&capacity.ScalingReason!="factory startup fund unavailable"&&
                spendable>=(long)TerritoryRules.Cost(p,PlayableBuildingKind.Refinery)+PlayableUnitRules.Cost(p,startupKind)+additionalProductionProtection;
            bool income=prepareOutpost||needsIncome||nextLineUseful&&!canSupportNext&&(capacity.BlockedExits==0||preferTwoProducers.HasValue)||spareIncome;
            if(!income&&(!nextLineUseful||!measured||capacity.SustainedSpendPerSecond<=0&&capacity.BlockedExits==0||
                capacity.BlockedExits>0&&!qualifiedDemand))return null;
            var kind=income?PlayableBuildingKind.Refinery:PlayableBuildingKind.Factory;
            if(income&&(TerritoryRules.Income(p,kind)<=0||!HasIncomeSlot(o,ai,preferTwoProducers.HasValue)))return null;
            var sites=o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested).ToArray();
            bool ownedOutpost=sites.Any(s=>s.Site.Kind==PlayableBuildingKind.Outpost);
            int FreeSlots(TerritorySiteSnapshot s)=>s.Site.Slots.Count(slot=>!o.Buildings.Any(b=>b.SiteId==s.Site.Id&&b.SlotId==slot.Id));
            foreach(var site in sites.OrderBy(s=>outpostProspect&&s.Site.Kind==PlayableBuildingKind.Outpost?0:s.Site.Id==o.HomeSiteId?1:2).ThenBy(s=>s.Site.Id))
            {
                // A territorial branch can develop income and production on the
                // outpost and leave the last headquarters slots for technology.
                if(income&&preferTwoProducers==true&&ownedOutpost&&site.Site.Id==o.HomeSiteId&&!HasScience(o)&&
                    FreeSlots(site)<=ai.Value("economy.reservedScienceSlots")+ai.Value("economy.reservedProductionSlots"))continue;
                if(income&&preferTwoProducers==true&&site.Site.Kind==PlayableBuildingKind.Outpost&&
                    !o.Buildings.Any(b=>b.SiteId==site.Site.Id&&b.Owner==o.Owner&&Live(b)&&Producer(b.Kind))&&
                    FreeSlots(site)<=ai.Value("economy.reservedProductionSlots"))continue;
                foreach(var slot in site.Site.Slots.OrderBy(s=>s.Id))
                {
                    var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,
                        siteId:site.Site.Id,slotId:slot.Id,parentId:site.CenterId,buildingKind:kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                    string rejection=MixRejection(o,p,ai,kind,site.Site.Id,groundKind:startupKind)??AiEconomyAdmission.Reject(o,p,action);
                    if(rejection!=null&&rejection!="insufficient credits")continue;
                    int quote=TerritoryRules.Cost(p,kind)+(income?0:checked((int)ai.Value("economy.factoryLaunchCycles")*PlayableUnitRules.Cost(p,startupKind)));
                    bool canKeepProtectedProductionQuote=additionalProductionProtection==0||
                        spendable>=(long)quote+additionalProductionProtection;
                    // Funding can precede utilization: spare money still loads old lines.
                    // Actual factory construction retains every existing scaling guard.
                    bool witnessed=qualifiedDemand&&demand.Action.SiteId==action.SiteId&&demand.Action.SlotId==action.SlotId&&demand.Action.ParentId==action.ParentId;
                    bool qualified=income||witnessed||capacity.CanScale||capacity.ScalingReason=="factory startup fund unavailable";
                    // Keep the bounded saving quote through a temporary release delay,
                    // but do not buy another producer while an existing exit is blocked.
                    string reason=!canKeepProtectedProductionQuote?"useful scout replacement takes priority over discretionary investment":rejection??(!income&&capacity.BlockedExits>0?capacity.ScalingReason:qualified?null:capacity.ScalingReason);
                    var candidate=new AiEconomyCandidate("economy",action,reason,1);
                    return new AiEconomicInvestment(candidate,qualified&&canKeepProtectedProductionQuote?quote:0,income?"fund income for existing productive capacity":qualified?"fund factory from previously qualified sustained demand":"load existing lines to qualify sustained factory demand",additionalProductionProtection);
                }
            }
            return null;
        }
    }
}

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
        internal static bool HasIncomeSlot(PlayableAiObservation o,AiProfile ai)
        {
            var sites=o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested).ToArray();
            int free=sites.Sum(s=>s.Site.Slots.Count(slot=>!o.Buildings.Any(b=>b.SiteId==s.Site.Id&&b.SlotId==slot.Id)));
            // Placement protection is tunable independently of money and demand.
            int science=o.Buildings.Any(b=>b.Owner==o.Owner&&b.Health>0&&b.Kind==PlayableBuildingKind.ScientificCenter)?0:(int)ai.Value("economy.reservedScienceSlots");
            return free>science+(int)ai.Value("economy.reservedProductionSlots");
        }
        public static AiEconomicInvestment Plan(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiProductionCapacity capacity,bool homeReady,bool emergency,AiBudgetEntry demand=null,bool initialOpeningCommitmentPending=false,int additionalProductionProtection=0,long availableCredits=-1)
        {
            if(additionalProductionProtection<0||availableCredits< -1)throw new ArgumentOutOfRangeException();
            if(!homeReady||emergency||initialOpeningCommitmentPending||capacity==null||
                capacity.SustainedSpendPerSecond<=0||capacity.BlockedExits>0||capacity.PopulationWaitingLines>0||o.Population==null||o.Population.Capacity-o.Population.Living-o.Population.Reserved<PlayableUnitRules.Population(p,PlayableEntityKind.Tank))return null;
            // Let each already paid investment finish before quoting its successor. Paid
            // unit queues also finish normally; pausing means withholding NEW orders.
            if(HasPendingConstruction(o,p))return null;
            // Feeding current lines is not a growth target. At equality, save for
            // more income so a subsequent productive line can be sustained.
            // Factory launch commitments are Tank cycles. A temporarily cheaper scout
            // or support head must not classify income sufficient for those cycles as
            // overflow and divert the replacement scout's money into another factory.
            double launchSpend=AiProductionDemand.Lines(o).Count()*PlayableUnitRules.Cost(p,PlayableEntityKind.Tank)/PlayableUnitRules.Duration(p,PlayableEntityKind.Tank);
            double spend=Math.Max(capacity.SustainedSpendPerSecond,launchSpend);
            bool income=o.IncomePerSecond<=spend;
            if(!income&&capacity.MeasuredTicks<AiProfile.SecondsToTicks(ai.Value("economy.incomeWindowSeconds"),30))return null;
            var kind=income?PlayableBuildingKind.Refinery:PlayableBuildingKind.Factory;
            if(income&&(TerritoryRules.Income(p,kind)<=0||!HasIncomeSlot(o,ai)))return null;
            foreach(var site in o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested).OrderBy(s=>s.Site.Id==o.HomeSiteId?0:1).ThenBy(s=>s.Site.Id))
            foreach(var slot in site.Site.Slots.OrderBy(s=>s.Id))
            {
                var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,
                    siteId:site.Site.Id,slotId:slot.Id,parentId:site.CenterId,buildingKind:kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                string rejection=AiEconomyAdmission.Reject(o,p,action);
                if(rejection!=null&&rejection!="insufficient credits")continue;
                int quote=TerritoryRules.Cost(p,kind)+(income?0:checked((int)ai.Value("economy.factoryLaunchCycles")*PlayableUnitRules.Cost(p,PlayableEntityKind.Tank)));
                long spendable=availableCredits<0?o.Credits:availableCredits;
                bool canKeepProtectedProductionQuote=additionalProductionProtection==0||spendable>=(long)quote+additionalProductionProtection;
                // Funding can precede utilization: spare money still loads old lines.
                // Actual factory construction retains every existing scaling guard.
                bool witnessed=demand!=null&&IsDemand(demand)&&demand.Amount==0&&demand.CreatedTick<=o.Tick&&demand.ExpiresTick>=o.Tick&&demand.Terms==p.ProfileId+"@"+p.Revision&&
                    demand.Action.PlayerId==o.OwnerId&&demand.Action.Generation==o.Generation&&demand.Action.SiteId==action.SiteId&&demand.Action.SlotId==action.SlotId;
                bool qualified=income||witnessed||capacity.CanScale||capacity.ScalingReason=="factory startup fund unavailable";
                string reason=!canKeepProtectedProductionQuote?"useful scout replacement takes priority over discretionary investment":rejection??(qualified?null:capacity.ScalingReason);
                var candidate=new AiEconomyCandidate("economy",action,reason,1);
                return new AiEconomicInvestment(candidate,qualified&&canKeepProtectedProductionQuote?quote:0,income?"fund income for existing productive capacity":qualified?"fund factory from previously qualified sustained demand":"load existing lines to qualify sustained factory demand",additionalProductionProtection);
            }
            return null;
        }
    }
}

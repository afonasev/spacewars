using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    public enum AiResearchProgress { Available, Waiting, Active, Completed }
    public sealed class InfrastructureRequest
    {
        internal InfrastructureRequest(PlayableResearchKind research,double utility){Research=research;Utility=utility;}
        public PlayableResearchKind Research{get;} public double Utility{get;}
        public PlayableBuildingKind Building=>AiResearchCatalog.For(Research).Prerequisite;
    }
    public sealed class AiResearchAssessment
    {
        internal AiResearchAssessment(AiResearchDescriptor d,AiResearchProgress progress,double affected,double utility,double missing,double wait)
        {Descriptor=d;Progress=progress;AffectedValue=affected;Utility=utility;MissingCost=missing;WaitingSeconds=wait;}
        public AiResearchDescriptor Descriptor{get;} public AiResearchProgress Progress{get;}
        public double AffectedValue{get;} public double Utility{get;} public double MissingCost{get;} public double WaitingSeconds{get;}
    }
    public sealed class AiTechnologyPlan
    {
        internal AiTechnologyPlan(AiResearchAssessment[] branches,InfrastructureRequest infrastructure,PlayableAiAction action,string reason)
        {Branches=Array.AsReadOnly(branches);Infrastructure=infrastructure;Action=action;Reason=reason;}
        public IReadOnlyList<AiResearchAssessment> Branches{get;} public InfrastructureRequest Infrastructure{get;}
        public PlayableAiAction Action{get;} public string Reason{get;}
    }
    // Pure proposals. Ordinary owner research orders are the durable progress authority:
    // an interrupted active order disappears; waiting orders survive and completed effects persist.
    // Pending callbacks and exact paid receipts remain in the existing C9 policy/shared ledger.
    public sealed class AiTechnologyPlanner
    {
        public static bool ProductivePrerequisiteReady(PlayableAiObservation o,AiResearchDescriptor research,AiRosterCatalog catalog=null)
        {
            catalog=catalog??AiRosterCatalog.Initial;var descriptor=catalog.For(research.Unit);
            return String.IsNullOrWhiteSpace(descriptor.exclusionReason)&&descriptor.unlockDependencies.All(kind=>o.OwnerResearch.Any(r=>r.Kind==kind&&r.Complete))&&
                AiProductionDemand.Lines(o,catalog,research.Unit).Any(b=>!AiProductionDemand.Blocked(b));
        }
        public AiTechnologyPlan Plan(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiBudgetState budget,bool emergency=false,AiRosterCatalog catalog=null)
        {
            if(o==null||p==null||ai==null||budget==null)throw new ArgumentNullException();
            if(budget.OwnerId!=o.OwnerId||budget.Generation!=o.Generation||p.ProfileId!=o.ProfileId||p.Revision!=o.ProfileRevision)throw new ArgumentException("Foreign technology context.");
            catalog=catalog??AiRosterCatalog.Initial;
            var centers=o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.PrivateState?.Lifecycle?.Selling!=true).OrderBy(b=>b.Id).ToArray();
            var center=centers.FirstOrDefault(b=>b.Phase==ConstructionPhase.Ready);
            double missing=centers.Length==0?TerritoryRules.Cost(p,PlayableBuildingKind.ScientificCenter):0;
            double construction=centers.Length==0?p.ScienceBuildSeconds:center==null?centers.Min(b=>Math.Max(0,1-b.Progress)*p.ScienceBuildSeconds):0;
            double wait=construction+o.OwnerResearch.Where(r=>!r.Complete).Sum(r=>r.Active?Math.Max(0,r.Duration-r.Elapsed):AiResearchCatalog.For(r.Kind).Duration(p));
            double horizon=ai.Value("utility.forceHorizonSeconds");
            var visible=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0).ToArray();
            var own=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0).ToArray();
            double ownForce=AiDefensePlanner.Force(own,p,ai),hostileForce=AiDefensePlanner.Force(visible,p,ai);
            double danger=ownForce+hostileForce==0?0:hostileForce/(ownForce+hostileForce);
            var branches=AiResearchCatalog.All.Select(d=>{
                var order=o.OwnerResearch.SingleOrDefault(r=>r.Kind==d.Kind);
                var progress=order==null?AiResearchProgress.Available:order.Complete?AiResearchProgress.Completed:order.Active?AiResearchProgress.Active:AiResearchProgress.Waiting;
                // Each paid queue and accepted unpaid production command contributes once; paid ledger history never does.
                double affected=own.Where(u=>u.Kind==d.Unit&&!u.Upgraded).Sum(u=>PlayableUnitRules.Cost(p,u.Kind)*Math.Min(1,u.Health/(double)PlayableUnitRules.Health(p,u.Kind)))+
                    o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&b.PrivateState!=null).SelectMany(b=>b.PrivateState.Orders).Where(q=>q.Kind==d.Unit).Sum(q=>q.PaidCost)+
                    budget.Unpaid.Where(e=>!e.AwaitingPayment&&PlayableUnitRules.ProductionKind(e.Action.Kind)==d.Unit).Sum(e=>(double)e.Amount);
                // One credit is the native ledger quantum used only for a finite free-upgrade denominator.
                // Credit-valued fractional gameplay gain; common time discount and visible force penalty.
                double utility=(progress!=AiResearchProgress.Available&&progress!=AiResearchProgress.Waiting)||emergency?0:affected*d.RelativeGain(p)/Math.Max(1,d.Cost(p)+missing)*horizon/(horizon+wait+d.Duration(p))*(1-danger);
                return new AiResearchAssessment(d,progress,affected,utility,missing,wait);
            }).ToArray();
            long available=Math.Max(0,(long)budget.Liquid-budget.SafetyReserve-budget.Reserved.Sum(e=>(long)e.Amount)-budget.Unpaid.Sum(e=>(long)e.Amount));
            // Existing-center proposals remain visible to shared admission/retry diagnostics.
            // Missing science must fit both construction and unheld future research cost;
            // a waiting order's already accepted unpaid amount is never reserved twice.
            var best=branches.Where(b=>b.Utility>0&&ProductivePrerequisiteReady(o,b.Descriptor,catalog)&&(b.Progress==AiResearchProgress.Waiting||o.ResearchAvailability.Any(r=>r.Kind==b.Descriptor.Kind&&r.Available))&&
                (center!=null?o.Credits>=b.Descriptor.Cost(p):available>=Math.Ceiling(missing+(b.Progress==AiResearchProgress.Waiting&&budget.Unpaid.Any(e=>e.AwaitingPayment&&e.Action.Kind==PlayableCommandKind.QueueResearch&&e.Action.ResearchKind==b.Descriptor.Kind)?0:b.Descriptor.Cost(p)))))
                .OrderByDescending(b=>b.Utility).ThenBy(b=>b.Descriptor.Cost(p)).ThenBy(b=>b.Descriptor.Kind).FirstOrDefault();
            if(best==null)return new AiTechnologyPlan(branches,null,null,emergency?"visible center threat: defer new technology":branches.Any(b=>b.Utility>0)&&!branches.Any(b=>b.Utility>0&&ProductivePrerequisiteReady(o,b.Descriptor,catalog))?"compatible productive prerequisite unresolved":"no affordable useful available research");
            if(center==null)return new AiTechnologyPlan(branches,centers.Length==0?new InfrastructureRequest(best.Descriptor.Kind,best.Utility):null,null,centers.Length==0?"request science prerequisite through shared macro":"science construction pending");
            if(best.Progress==AiResearchProgress.Waiting)return new AiTechnologyPlan(branches,null,null,"ordinary waiting research owns its existing order/receipt");
            var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:best.Descriptor.Kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            return new AiTechnologyPlan(branches,null,action,"highest gameplay payoff research");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    [Serializable] public sealed class AiOpeningExecutionState
    {
        public string Template{get;internal set;} public PlayableAiOpeningPhase Phase{get;internal set;}
        public long StartedTick{get;internal set;}=-1;
        public long ProgressTick{get;internal set;}=-1;
        public int Replans{get;internal set;} public string Reason{get;internal set;}="not started";
        public IReadOnlyList<string> Completed{get;internal set;}=Array.Empty<string>();
        public string Fallback=>Phase==PlayableAiOpeningPhase.Aborted?"ordinary-macro-army":null;
    }
    // A detached request executor: macro requests enter the existing economy policy and
    // pressure enters ArmyPlanner/Registry. It never owns commands, funds or tactical units.
    public sealed class AiOpeningExecutor
    {
        private readonly string owner;private readonly long generation;
        private readonly AiOpeningDefinition definition;
        private readonly HashSet<string> completed=new HashSet<string>(StringComparer.Ordinal);
        private long started=-1,progress=-1,pressureReceipt=-1;
        private int replans,producer,expansionSite;
        private PlayableAiOpeningPhase phase=PlayableAiOpeningPhase.Active;
        private string reason="not started";
        public AiOpeningExecutor(PlayableAiOpeningCompositionState opening,long generation)
        {owner=opening.OwnerId;this.generation=generation;definition=AiOpeningCatalog.For(opening.Opening);}
        public AiOpeningExecutionState Capture()=>new AiOpeningExecutionState{Template=definition.Id,Phase=phase,StartedTick=started,ProgressTick=progress,Replans=replans,Reason=reason,Completed=definition.Milestones.Where(m=>completed.Contains(m.Id)).Select(m=>m.Id).ToArray()};
        private bool Enabled(AiOpeningMilestone m)=>!completed.Contains(m.Id)&&m.Dependencies.All(completed.Contains);
        private bool Wants(AiOpeningGoal goal)=>phase==PlayableAiOpeningPhase.Active&&definition.Milestones.Any(m=>m.Goal==goal&&Enabled(m));
        public AiScoutPlan ScoutPlan=>phase!=PlayableAiOpeningPhase.Active?AiScoutPlan.Standard:definition.Opening==PlayableAiOpening.BlindRush?AiScoutPlan.BlindRush:definition.Opening==PlayableAiOpening.ExplorerAllIn?AiScoutPlan.ScoutLed:AiScoutPlan.Standard;
        // Existing opportunistic C4 expansion remains live; this template adds a mandatory center milestone.
        public bool AllowsExpansion=>true;
        public bool AllowsPressure=>phase!=PlayableAiOpeningPhase.Active||Wants(AiOpeningGoal.Pressure);
        private void Abort(string why){phase=PlayableAiOpeningPhase.Aborted;reason=why+"; fallback "+definition.Fallback;}
        private void Replan(long tick,AiProfile ai,string why)
        {
            replans++;completed.Clear();producer=0;expansionSite=0;pressureReceipt=-1;progress=tick;
            reason="replan: "+why;
            if(replans>ai.Value("decision.retryLimit"))Abort("prerequisite retry limit: "+why);
        }
        public void Observe(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyDeployment deployment,AiProfile ai)
        {
            if(o.OwnerId!=owner||o.Generation!=generation)throw new ArgumentException("Opening observation binding");
            if(knowledge!=null&&(knowledge.OwnerId!=owner||knowledge.Generation!=generation||knowledge.ObservationTick!=o.Tick))throw new ArgumentException("Opening knowledge binding");
            if(deployment!=null&&(deployment.OwnerId!=owner||deployment.Generation!=generation||deployment.Seed!=o.Seed||deployment.ProfileId!=o.ProfileId||deployment.ProfileRevision!=o.ProfileRevision||deployment.ObservedTick!=o.Tick||deployment.StartedTick>o.Tick||deployment.ArmyId<=0))throw new ArgumentException("Opening deployment binding");
            if(phase!=PlayableAiOpeningPhase.Active)return;
            if(started<0){started=o.Tick;progress=o.Tick;reason="executing";}
            if(o.Tick-started>=AiProfile.SecondsToTicks(ai.Value("opening.deadlineSeconds"),30)){Abort("opening deadline reached");return;}
            if(producer!=0&&!o.Buildings.Any(b=>b.Id==producer&&b.Owner==o.Owner&&b.Health>0&&b.PrivateState?.Lifecycle?.Selling!=true))Replan(o.Tick,ai,"producer lost");
            if(expansionSite!=0&&!o.Sites.Any(s=>s.Site.Id==expansionSite&&s.Owner==o.Owner&&s.Ready))Replan(o.Tick,ai,"expansion site lost");
            if(phase!=PlayableAiOpeningPhase.Active)return;
            if(completed.Contains("income")&&o.Buildings.Count(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Refinery&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true)<IncomeCount(ai))Replan(o.Tick,ai,"income prerequisite lost");
            if(completed.Contains("force")&&o.Entities.Count(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Tank)<ai.Value("opening.forceUnits"))Replan(o.Tick,ai,"opening force lost");
            if(phase!=PlayableAiOpeningPhase.Active)return;
            foreach(var m in definition.Milestones.Where(Enabled))
            {
                bool success=false;
                switch(m.Goal)
                {
                    case AiOpeningGoal.Factory:
                        var factory=o.Buildings.Where(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true).OrderBy(b=>b.Id).FirstOrDefault();
                        success=factory!=null;if(success)producer=factory.Id;break;
                    case AiOpeningGoal.Income:
                        success=o.Buildings.Count(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Refinery&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true)>=IncomeCount(ai);break;
                    case AiOpeningGoal.Expansion:
                        var site=o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&s.Site.Id!=o.HomeSiteId&&o.Buildings.Any(b=>b.Id==s.CenterId&&b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready)).OrderBy(s=>s.Site.Id).FirstOrDefault();
                        success=site!=null;if(success)expansionSite=site.Site.Id;break;
                    case AiOpeningGoal.Force:
                        success=o.Entities.Count(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Tank)>=ai.Value("opening.forceUnits");break;
                    case AiOpeningGoal.Intel:
                        success=knowledge!=null&&knowledge.Areas.Any(v=>v.ActuallyCoveredTick>=started&&o.PublicScoutObjectives.Any(g=>g.SiteId==v.AreaId&&g.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart))||o.Buildings.Any(b=>o.IsHostile(b.Owner)&&b.Health>0);break;
                    case AiOpeningGoal.Pressure:
                        success=deployment!=null&&pressureReceipt>=deployment.StartedTick&&pressureReceipt<=o.Tick&&deployment.StartedTick>=started;break;
                }
                if(success){completed.Add(m.Id);progress=o.Tick;reason="world fact completed "+m.Id;}
            }
            foreach(var m in definition.Milestones.Where(Enabled).Where(m=>m.Goal==AiOpeningGoal.Factory||m.Goal==AiOpeningGoal.Income))
            {
                var kind=m.Goal==AiOpeningGoal.Factory?PlayableBuildingKind.Factory:PlayableBuildingKind.Refinery;
                int required=m.Goal==AiOpeningGoal.Factory?1:IncomeCount(ai);
                int built=o.Buildings.Count(b=>b.Owner==o.Owner&&b.Kind==kind&&b.Health>0&&b.PrivateState?.Lifecycle?.Selling!=true);
                bool free=o.Sites.Any(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested&&s.Site.Slots.Any(slot=>!o.Buildings.Any(b=>b.SiteId==s.Site.Id&&b.SlotId==slot.Id)));
                if(built<required&&!free){Abort("no owned slot for "+m.Id);return;}
            }
            if(completed.Count==definition.Milestones.Count){phase=PlayableAiOpeningPhase.Complete;reason="all milestones completed";}
        }
        private int IncomeCount(AiProfile ai)=>definition.Opening==PlayableAiOpening.GreedySafe?(int)ai.Value("opening.greedyRefineries"):1;
        public void ObserveReceipt(PlayableAiAction action,PlayableAiTraceRecord receipt)
        {
            if(phase!=PlayableAiOpeningPhase.Active||action==null||action.PlayerId!=owner||action.Generation!=generation||receipt==null||receipt.OwnerId!=owner||receipt.ReceiptIdentity?.OwnerId!=owner||receipt.ReceiptIdentity.Generation!=generation||receipt.Kind!=action.Kind||receipt.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity)return;
            if(receipt.Policy==AiArmyPlanner.Policy&&(action.Kind==PlayableCommandKind.Attack||action.Kind==PlayableCommandKind.AttackMove)&&receipt.Status==PlayableAiDeliveryStatus.Applied&&receipt.RuntimeStatus==PlayableCommandStatus.Applied)
                pressureReceipt=receipt.ApplicationTick;
            else if(receipt.Status==PlayableAiDeliveryStatus.Rejected||receipt.Status==PlayableAiDeliveryStatus.Cancelled)reason="request terminal: "+receipt.Message;
        }
        public IReadOnlyList<AiEconomyCandidate> MacroRequests(PlayableAiObservation o,PlayableProfile p,AiProfile ai,IEnumerable<AiEconomyCandidate> normal)
        {
            if(phase!=PlayableAiOpeningPhase.Active)return normal.ToArray();
            var requests=new List<AiEconomyCandidate>();
            foreach(var kind in new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery})
            {
                bool wanted=kind==PlayableBuildingKind.Factory?Wants(AiOpeningGoal.Factory):Wants(AiOpeningGoal.Income);
                int count=kind==PlayableBuildingKind.Factory?1:IncomeCount(ai);
                if(!wanted||o.Buildings.Count(b=>b.Owner==o.Owner&&b.Kind==kind&&b.Health>0&&b.PrivateState?.Lifecycle?.Selling!=true)>=count)continue;
                foreach(var site in o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested).OrderBy(s=>s.Site.Id==o.HomeSiteId?0:1).ThenBy(s=>s.Site.Id))
                    foreach(var slot in site.Site.Slots.OrderBy(s=>s.Id))
                    {
                        var a=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:site.Site.Id,slotId:slot.Id,parentId:site.CenterId,buildingKind:kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                        requests.Add(new AiEconomyCandidate("economy",a,AiEconomyAdmission.Reject(o,p,a,true),1));
                    }
            }
            bool explorer=Wants(AiOpeningGoal.Expansion)||Wants(AiOpeningGoal.Intel);
            if(explorer&&!o.Entities.Any(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer)&&!o.Buildings.Any(b=>b.Owner==o.Owner&&b.PrivateState!=null&&b.PrivateState.Orders.Any(q=>q.Kind==PlayableEntityKind.Explorer)))
                foreach(var factory in AiProductionDemand.Lines(o))
                {
                    var a=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueExplorer,new[]{factory.Id},unitKind:PlayableEntityKind.Explorer,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                    requests.Add(new AiEconomyCandidate("production",a,AiEconomyAdmission.Reject(o,p,a,true),1));
                }
            requests.AddRange(normal.Where(c=>c.Policy=="production"));
            if(definition.Opening!=PlayableAiOpening.GreedySafe&& !o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Refinery)&&
                (completed.Contains("factory")||!requests.Any(c=>c.Legal)))
                requests.AddRange(normal.Where(c=>c.Action.Kind==PlayableCommandKind.BuildAt&&c.Action.BuildingKind==PlayableBuildingKind.Refinery));
            // An already captured expansion still needs its ordinary center build.
            if(Wants(AiOpeningGoal.Expansion))requests.AddRange(normal.Where(c=>c.Action.Kind==PlayableCommandKind.BuildAt&&c.Action.BuildingKind==PlayableBuildingKind.Mine));
            return requests.GroupBy(c=>AiEconomyAdmission.IntentId(c.Policy,c.Action)).Select(g=>g.First()).ToArray();
        }
        internal void WriteState(BinaryWriter w)
        {
            WorldWire.String(w,definition.Id);w.Write((int)phase);w.Write(started);w.Write(progress);w.Write(pressureReceipt);w.Write(replans);w.Write(producer);w.Write(expansionSite);WorldWire.String(w,reason);
            WorldWire.Array(w,definition.Milestones.Where(m=>completed.Contains(m.Id)).Select(m=>m.Id).ToArray(),s=>WorldWire.String(w,s));
        }
        internal void ReadState(BinaryReader r,long tick)
        {
            AiStateWire.Require(WorldWire.String(r)==definition.Id,"opening template binding");phase=WorldWire.EnumValue<PlayableAiOpeningPhase>(r);started=r.ReadInt64();progress=r.ReadInt64();pressureReceipt=r.ReadInt64();replans=r.ReadInt32();producer=r.ReadInt32();expansionSite=r.ReadInt32();reason=WorldWire.String(r);
            AiStateWire.Require(started>=-1&&started<=tick&&progress>=started&&progress<=tick&&pressureReceipt>=-1&&pressureReceipt<=tick&&replans>=0&&producer>=0&&expansionSite>=0&&!string.IsNullOrWhiteSpace(reason),"opening execution clocks/values");
            foreach(var id in WorldWire.Array(r,()=>WorldWire.String(r)))AiStateWire.Require(definition.Milestones.Any(m=>m.Id==id)&&completed.Add(id),"opening milestone identity");
            AiStateWire.Require(definition.Milestones.Where(m=>completed.Contains(m.Id)).All(m=>m.Dependencies.All(completed.Contains))&& (phase!=PlayableAiOpeningPhase.Complete||completed.Count==definition.Milestones.Count),"opening DAG progress");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Pure detached decision data. No domain, reservations, policy allocators or hidden occupancy.
    public sealed class AiEconomyCandidate
    {
        public AiEconomyCandidate(string policy,PlayableAiAction action,string reason,int priority=0)
        {Policy=policy;Action=action;Reason=reason;Priority=priority;}
        public string Policy{get;} public PlayableAiAction Action{get;} public string Reason{get;}
        public bool Legal=>Reason==null; public int Priority{get;}
    }
    public sealed class AiEconomyPlan
    {
        internal AiEconomyPlan(IEnumerable<AiEconomyCandidate> candidates,IEnumerable<int> expansionSites,string expansionReason,AiProductionCapacity capacity=null)
        {Capacity=capacity;Candidates=Array.AsReadOnly(candidates.ToArray());ExpansionSiteIds=Array.AsReadOnly(expansionSites.ToArray());ExpansionReason=expansionReason;}
        public AiProductionCapacity Capacity{get;}
        public IReadOnlyList<AiEconomyCandidate> Candidates{get;}
        // A request for the later expansion planner, never a capture command/claim here.
        public IReadOnlyList<int> ExpansionSiteIds{get;} public string ExpansionReason{get;}
    }
    public sealed class AiEconomyPlanner
    {
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        public AiEconomyPlan Plan(PlayableAiObservation o,PlayableProfile profile,AiProductionDemand demand=null,AiProfile ai=null,long? available=null,bool recoveryExplorerDemand=false)
        {
            if(o==null||profile==null)throw new ArgumentNullException();
            var candidates=new List<AiEconomyCandidate>();
            var capacity=demand?.Assess(o,profile,ai,available??o.Credits);
            void Add(string policy,PlayableAiAction action,string reason=null,int priority=0)=>candidates.Add(new AiEconomyCandidate(policy,action,AiEconomyAdmission.Reject(o,profile,action,true)??reason,priority));
            PlayableAiAction Build(int site,int slot,int parent,PlayableBuildingKind kind)=>new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:site,slotId:slot,parentId:parent,buildingKind:kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var home=o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&(s.Site.Kind==PlayableBuildingKind.Headquarters||s.Site.Kind==PlayableBuildingKind.Outpost)&&o.Buildings.Any(b=>b.Id==s.CenterId&&b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true))
                .OrderBy(s=>s.Site.Id==o.HomeSiteId?0:1).ThenBy(s=>s.Site.Id).FirstOrDefault();
            bool full=false;
            if(home!=null&&home.Owner==o.Owner)
            {
                foreach(var kind in new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery})
                {
                    if(o.Buildings.Any(b=>b.Owner==o.Owner&&b.SiteId==home.Site.Id&&b.Kind==kind))continue;
                    var slots=home.Site.Slots.OrderBy(s=>s.Id).ToArray();
                    full=slots.All(s=>o.Buildings.Any(b=>b.SiteId==home.Site.Id&&b.SlotId==s.Id));
                    foreach(var slot in slots)Add("economy",Build(home.Site.Id,slot.Id,home.CenterId,kind),priority:kind==PlayableBuildingKind.Factory&&!o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory)?1:0);
                }
            }
            foreach(var site in o.Sites.Where(s=>s.Site.Kind==PlayableBuildingKind.Mine&&(s.Owner==o.Owner||s.Claimant==o.Owner)&&s.CenterId==0).OrderBy(s=>s.Site.Id))
                Add("economy",Build(site.Site.Id,0,0,PlayableBuildingKind.Mine));
            // Scale only observed owned legal slots, never infer public occupancy. A paid
            // construction suppresses further scaling until its new line can be used.
            if(capacity!=null&&o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory))
                foreach(var site in o.Sites.Where(s=>s.Owner==o.Owner).OrderBy(s=>s.Site.Id))
                    foreach(var slot in site.Site.Slots.OrderBy(s=>s.Id))
                        Add("economy",Build(site.Site.Id,slot.Id,site.CenterId,PlayableBuildingKind.Factory),capacity.ScalingReason);
            // Preserve current composition pending S4, fill idle lines before scaling.
            foreach(var factory in o.Buildings.Where(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory).OrderBy(b=>b.Id))
                Add("production",new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueTank,new[]{factory.Id},unitKind:PlayableEntityKind.Tank,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity),priority:demand!=null&&demand.IdleTicks(factory.Id,o.Tick)>=AiProfile.SecondsToTicks(ai.Value("economy.idleLineDeadlineSeconds"),30)?1:0);
            // Preserve normal Tank composition. This bounded affordable alternative restores
            // a missing existing scout role, never buys extra Explorers to drain a bank.
            if(recoveryExplorerDemand&&!o.Entities.Any(e=>e.Owner==o.Owner&&e.Kind==PlayableEntityKind.Explorer&&e.Health>0)&&
                !o.Buildings.Any(b=>b.Owner==o.Owner&&b.PrivateState!=null&&b.PrivateState.Orders.Any(q=>q.Kind==PlayableEntityKind.Explorer))&&
                o.PublicScoutObjectives.Any(g=>g.Reachable&&g.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart)&&
                (available??o.Credits)>=profile.ExplorerCreditCost&&(available??o.Credits)<profile.TankCreditCost)
                foreach(var factory in o.Buildings.Where(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready&&b.Health>0&&b.PrivateState!=null&&b.PrivateState.QueueCount==0&&b.PrivateState.Lifecycle?.Selling!=true).OrderBy(b=>b.Id))
                {
                    bool danger=o.Entities.Any(e=>o.IsHostile(e.Owner)&&e.Health>0&&Distance(e.Position,factory.Position)<=PlayableUnitRules.Vision(profile,PlayableEntityKind.Explorer))||o.Buildings.Any(b=>o.IsHostile(b.Owner)&&b.Health>0&&Distance(b.Position,factory.Position)<=PlayableUnitRules.Vision(profile,PlayableEntityKind.Explorer));
                    if(!danger)Add("production",new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueExplorer,new[]{factory.Id},unitKind:PlayableEntityKind.Explorer,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity),priority:1);
                }
            var expansion=full?o.PublicScoutObjectives.Where(s=>s.Reachable&&
                !o.Sites.Any(k=>k.Site.Id==s.SiteId&&(k.Owner==o.Owner||k.Contested||k.Owner.HasValue))).OrderBy(s=>s.SiteId).Select(s=>s.SiteId).ToArray():Array.Empty<int>();
            return new AiEconomyPlan(candidates,expansion,full?(expansion.Length>0?"local slots occupied; request reconnaissance of public reachable expansion; danger/occupancy unverified":"local slots occupied; no reachable expansion objective; conversion requires later infrastructure evaluation"):null,capacity);
        }
    }
    public sealed class AiEconomyCandidateDecision
    {
        internal AiEconomyCandidateDecision(AiEconomyCandidate candidate,PlayableProfile profile,IReadOnlyList<AiIntent> selected,IReadOnlyDictionary<string,string> rejections)
        {
            var a=candidate.Action;Id=AiEconomyAdmission.IntentId(candidate.Policy,a);Policy=candidate.Policy;Kind=a.Kind;
            EntityIds=Array.AsReadOnly(a.CopyEntityIds());SiteId=a.SiteId;SlotId=a.SlotId;BuildingKind=a.BuildingKind;UnitKind=a.UnitKind;
            Credits=a.Kind==PlayableCommandKind.BuildAt?TerritoryRules.Cost(profile,a.BuildingKind):a.Kind==PlayableCommandKind.SellBuilding||a.Kind==PlayableCommandKind.StartBuildingRepair?0:PlayableUnitRules.Cost(profile,a.UnitKind);
            Population=a.Kind==PlayableCommandKind.QueueTank||a.Kind==PlayableCommandKind.QueueExplorer||a.Kind==PlayableCommandKind.QueueShkval?PlayableUnitRules.Population(profile,a.UnitKind):0;
            Selected=selected.Any(i=>i.Id==Id);
            Reason=candidate.Reason??(Selected?null:rejections.TryGetValue(Id,out var reason)?reason:"proposal unavailable");
        }
        public string Id{get;} public string Policy{get;} public PlayableCommandKind Kind{get;}
        public IReadOnlyList<int> EntityIds{get;} public int SiteId{get;} public int SlotId{get;}
        public PlayableBuildingKind BuildingKind{get;} public PlayableEntityKind UnitKind{get;}
        public int Credits{get;} public int Population{get;} public bool Selected{get;} public string Reason{get;}
    }
    // Latest detached diagnostic only: no strategy/claims/callbacks, no extra authority
    // command or attention charge. Worker history is bounded by its existing TraceLimit.
    public sealed class AiEconomyDecision
    {
        internal AiEconomyDecision(PlayableAiObservation o,PlayableProfile profile,Spacewars.Simulation.Ai.AiProfile ai,long ordinal,AiEconomyPlan plan,
            IReadOnlyList<AiIntent> selected,IReadOnlyDictionary<string,string> rejections,AiBudgetLedger budget)
        {
            ProductionCapacity=plan.Capacity;OwnerId=o.OwnerId;Generation=o.Generation;Tick=o.Tick;Ordinal=ordinal;ObservationIdentity=o.Identity;
            ProfileId=o.ProfileId;ProfileRevision=o.ProfileRevision;AiProfileId=ai.Id;AiProfileRevision=ai.Revision;AiProfileHash=ai.Hash;
            StartupReservations=Array.AsReadOnly(budget.Capture().Reserved.Where(e=>e.Purpose=="factory-startup").ToArray());
            Liquid=budget.Liquid;Reserved=budget.Reserved;Unpaid=budget.Unpaid;SafetyReserve=budget.SafetyReserve;Available=budget.Available;
            Living=o.Population?.Living??0;PopulationReserved=o.Population?.Reserved??0;Capacity=o.Population?.Capacity??0;
            Candidates=Array.AsReadOnly(plan.Candidates.Select(c=>new AiEconomyCandidateDecision(c,profile,selected,rejections)).ToArray());
            ExpansionSiteIds=Array.AsReadOnly(plan.ExpansionSiteIds.ToArray());ExpansionReason=plan.ExpansionReason;
            Blockers=Array.AsReadOnly(Candidates.Where(c=>!c.Selected).Select(c=>c.Reason).Concat(StartupReservations.Select(e=>"factory startup held "+e.Amount+" until tick "+e.ExpiresTick+(e.Action.Kind==PlayableCommandKind.QueueTank?" for producer "+string.Join(",",e.Action.EntityIds):" for site/slot "+e.Action.SiteId+"/"+e.Action.SlotId))).Concat(plan.ExpansionReason==null?Array.Empty<string>():new[]{plan.ExpansionReason}).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray());
            Reason=Candidates.Any(c=>c.Selected)?"selected known legal economy action":plan.Candidates.Any(c=>c.Legal)?"economy arbitration blocked; see candidate rejection reasons":Candidates.Count==0?"no known useful build site or producer":"no legal useful economy action; see candidate rejection reasons";
        }
        public IReadOnlyList<AiBudgetEntry> StartupReservations{get;}
        public AiProductionCapacity ProductionCapacity{get;}
        public string Identity=>Generation+":"+OwnerId+":"+Ordinal;
        public string OwnerId{get;} public long Generation{get;} public long Tick{get;} public long Ordinal{get;}
        public string ObservationIdentity{get;} public string ProfileId{get;} public int ProfileRevision{get;}
        public string AiProfileId{get;} public int AiProfileRevision{get;} public string AiProfileHash{get;}
        public int Liquid{get;} public long Reserved{get;} public long Unpaid{get;} public int SafetyReserve{get;} public long Available{get;}
        public int Living{get;} public int PopulationReserved{get;} public int Capacity{get;}
        public IReadOnlyList<AiEconomyCandidateDecision> Candidates{get;} public IReadOnlyList<int> ExpansionSiteIds{get;}
        public IReadOnlyList<string> Blockers{get;} public string ExpansionReason{get;} public string Reason{get;}
    }
    public static class AiEconomyAdmission
    {
        internal static string IntentId(string policy,PlayableAiAction a)=>policy+":"+a.Kind+":"+string.Join(",",a.EntityIds.OrderBy(x=>x))+":"+a.SiteId+":"+a.SlotId+":"+a.TargetId+":"+a.Target.X+":"+a.Target.Z+":"+a.BuildingKind+":"+a.UnitKind+":"+a.ResearchKind;
        public static string Reject(PlayableAiObservation o,PlayableProfile profile,PlayableAiAction a,bool idleProducer=false)
        {
            if(a.PlayerId!=o.OwnerId||a.Generation!=o.Generation||a.ProfileId!=profile.ProfileId||a.ProfileRevision!=profile.Revision||o.ProfileId!=profile.ProfileId||o.ProfileRevision!=profile.Revision)return "owner/generation/profile mismatch";
            if(a.Kind==PlayableCommandKind.BuildAt)
            {
                var site=o.Sites.FirstOrDefault(s=>s.Site.Id==a.SiteId);
                if(site==null)return "site not known";
                if(a.SlotId==0)
                {
                    if(a.BuildingKind!=site.Site.Kind||site.CenterId!=0||site.Progress<1||site.Claimant!=o.Owner||site.Owner.HasValue&&site.Owner!=o.Owner||a.ParentId!=0)return "site capture/center prerequisite";
                }
                else
                {
                    if(site.Owner!=o.Owner)return "site not owned";
                    if(a.BuildingKind!=PlayableBuildingKind.Factory&&a.BuildingKind!=PlayableBuildingKind.Refinery&&a.BuildingKind!=PlayableBuildingKind.ScientificCenter)return "unsupported slot building";
                    if(!site.Site.Slots.Any(s=>s.Id==a.SlotId))return "slot unavailable";
                    if(o.Buildings.Any(b=>b.SiteId==a.SiteId&&b.SlotId==a.SlotId))return "slot occupied";
                    var parent=o.Buildings.FirstOrDefault(b=>b.Id==site.CenterId&&b.Id==a.ParentId&&b.Owner==o.Owner);
                    if(parent==null||parent.Health<=0||parent.Phase!=ConstructionPhase.Ready||parent.PrivateState==null||parent.PrivateState.Lifecycle?.Selling==true)return "ready live parent prerequisite";
                }
                if(site.Contested)return "site contested";
                return o.Credits<TerritoryRules.Cost(profile,a.BuildingKind)?"insufficient credits":null;
            }
            if(a.Kind==PlayableCommandKind.QueueTank||a.Kind==PlayableCommandKind.QueueExplorer||a.Kind==PlayableCommandKind.QueueShkval)
            {
                var kind=a.Kind==PlayableCommandKind.QueueExplorer?PlayableEntityKind.Explorer:a.Kind==PlayableCommandKind.QueueShkval?PlayableEntityKind.Shkval:a.UnitKind;
                if(!PlayableUnitRules.Supported(kind)||a.EntityIds.Count!=1)return "unsupported production";
                var producer=o.Buildings.FirstOrDefault(b=>b.Id==a.EntityIds[0]&&b.Owner==o.Owner);
                if(producer==null||producer.Kind!=PlayableBuildingKind.Factory||producer.Health<=0||producer.Phase!=ConstructionPhase.Ready||producer.PrivateState==null||producer.PrivateState.Lifecycle?.Selling==true)return "ready live producer prerequisite";
                if(producer.PrivateState.QueueCount>=PlayableDomain.MaximumProductionOrders)return "producer queue full";
                if(idleProducer&&producer.PrivateState.QueueCount>0)return producer.PrivateState.Orders.Any(q=>q.Active&&q.Remaining<=0)?"producer exit blocked":"producer queue busy";
                if(o.Population==null||o.Population.Capacity-o.Population.Living-o.Population.Reserved<PlayableUnitRules.Population(profile,kind))return "population capacity";
                return o.Credits<PlayableUnitRules.Cost(profile,kind)?"insufficient credits":null;
            }
            if(a.Kind==PlayableCommandKind.StartBuildingRepair||a.Kind==PlayableCommandKind.SellBuilding)
            {
                if(a.EntityIds.Count!=1)return "one infrastructure recipient required";
                var b=o.Buildings.FirstOrDefault(x=>x.Id==a.EntityIds[0]&&x.Owner==o.Owner);
                var life=b?.PrivateState?.Lifecycle;
                if(b==null||b.Health<=0||b.Phase!=ConstructionPhase.Ready||life==null)return "ready owned infrastructure required";
                if(a.Kind==PlayableCommandKind.SellBuilding)return life.SaleBlockedReason;
                return life.RepairBlockedReason??(o.Credits<(int)Math.Ceiling(life.RepairAllocation)?"repair allocation unavailable":null);
            }
            return null;
        }
    }
}

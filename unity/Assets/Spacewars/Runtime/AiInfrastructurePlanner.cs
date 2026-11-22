using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using static Spacewars.Runtime.WorldWire;

namespace Spacewars.Runtime
{
    public sealed class AiInfrastructureState
    {
        internal AiInfrastructureState(int site,int slot,int parent,int victim,PlayableBuildingKind source,PlayableBuildingKind replacement,long created,long expires,bool settled,string reason)
        {SiteId=site;SlotId=slot;ParentId=parent;VictimId=victim;SourceKind=source;ReplacementKind=replacement;CreatedTick=created;ExpiresTick=expires;SaleSettled=settled;Reason=reason;}
        public int SiteId{get;} public int SlotId{get;} public int ParentId{get;} public int VictimId{get;}
        public PlayableBuildingKind SourceKind{get;} public PlayableBuildingKind ReplacementKind{get;}
        public long CreatedTick{get;} public long ExpiresTick{get;} public bool SaleSettled{get;} public string Reason{get;}
    }
    // Detached owner policy. Sale intent owns one physical slot through demolition and
    // replacement. Quotes are observations; only ordinary Applied receipts settle commands.
    public sealed class AiInfrastructurePlanner
    {
        internal const string ConversionPurpose="infrastructure-conversion";
        internal const string ExternalRepairPurpose="external-repair";
        private readonly string ownerId;
        private long nextId=1,pendingId,pendingGeneration;
        private int site,slot,parent,victim;
        private PlayableBuildingKind source,replacement;
        private long created,expires;
        private bool settled;
        private PlayableCommandKind pendingKind;
        private string reason="no conversion";
        public AiInfrastructurePlanner(string ownerId){this.ownerId=ownerId??throw new ArgumentNullException(nameof(ownerId));}
        internal AiInfrastructurePlanner Fork()=>(AiInfrastructurePlanner)MemberwiseClone();
        internal long PendingId=>pendingId; internal long PendingGeneration=>pendingGeneration;
        public AiInfrastructureState Capture()=>new AiInfrastructureState(site,slot,parent,victim,source,replacement,created,expires,settled,reason);
        private string HoldId=>ConversionPurpose+":"+site+":"+slot;
        private static string Terms(PlayableProfile p)=>p.ProfileId+"@"+p.Revision;
        public bool ClaimsSlot(int siteId,int slotId)=>site!=0&&site==siteId&&slot==slotId;
        internal string ReservationFor(PlayableAiAction a,AiBudgetLedger b)=>a.Kind==PlayableCommandKind.BuildAt&&ClaimsSlot(a.SiteId,a.SlotId)&&a.BuildingKind==replacement&&b.ReservationEntry(HoldId)!=null?HoldId:null;
        private static bool Useful(PlayableAiObservation o,PlayableProfile p,PlayableBuildingSnapshot b)=>TerritoryRules.Center(b.Kind)||b.Kind==PlayableBuildingKind.Factory||TerritoryRules.Income(p,b.Kind)>0||
            b.Kind==PlayableBuildingKind.ScientificCenter&&(b.PrivateState.Research.Any(r=>!r.Complete)||o.ResearchAvailability.Any(r=>r.Available));
        private static bool Danger(PlayableAiObservation o,PlayableProfile p,PlayableBuildingSnapshot b)=>o.Entities.Any(e=>o.IsHostile(e.Owner)&&e.Health>0&&
            Math.Sqrt(Math.Pow(e.Position.X-b.Position.X,2)+Math.Pow(e.Position.Z-b.Position.Z,2))<=p.TankVisionRange+TerritoryRules.Radius(p,b.Kind));
        private PlayableAiAction Action(PlayableAiObservation o,PlayableCommandKind kind,int id=0,PlayableBuildingKind target=PlayableBuildingKind.Factory,int siteId=0,int slotId=0,int parentId=0)=>
            new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,kind,id==0?System.Array.Empty<int>():new[]{id},siteId:siteId,slotId:slotId,parentId:parentId,buildingKind:target,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        public IReadOnlyList<AiEconomyCandidate> Plan(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiProductionCapacity capacity=null,
            long discretionaryCash=0,PlayableEntityKind startupKind=PlayableEntityKind.Tank,InfrastructureRequest scienceGoal=null)
        {
            if(o.OwnerId!=ownerId)throw new ArgumentException("Foreign infrastructure observation.");
            var result=new List<AiEconomyCandidate>();
            foreach(var b in o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle!=null).OrderBy(b=>b.Id))
            {
                var life=b.PrivateState.Lifecycle;
                if(life.Repairing||life.Selling)continue;
                bool priorityBoostRepair=b.BoostWearDamage&&b.RefineryUpgraded;
                if(b.BoostWearDamage&&!priorityBoostRepair&&
                    discretionaryCash<Math.Ceiling(life.RepairAllocation)+PlayableUnitRules.Cost(p,PlayableEntityKind.Tank))continue;
                if(!b.BoostWearDamage&&!Useful(o,p,b))continue;
                var a=Action(o,PlayableCommandKind.StartBuildingRepair,b.Id);
                // Useful healthy buildings remain diagnostic rejects, not actionable spam.
                result.Add(new AiEconomyCandidate("infrastructure",a,AiEconomyAdmission.Reject(o,p,a),
                    priorityBoostRepair||!b.BoostWearDamage&&(double)b.Health/TerritoryRules.MaximumHealth(p,b)<=ai.Value("economy.repairPriorityHealthRatio")?1:0));
            }
            if(site!=0)
            {
                if(settled&&!o.Buildings.Any(b=>b.SiteId==site&&b.SlotId==slot))
                {var a=Action(o,PlayableCommandKind.BuildAt,target:replacement,siteId:site,slotId:slot,parentId:parent);result.Add(new AiEconomyCandidate("infrastructure",a,AiEconomyAdmission.Reject(o,p,a),1));}
                return result.AsReadOnly();
            }
            foreach(var home in o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested&&s.Site.Slots.Count>0).OrderBy(s=>s.Site.Id))
            {
                var local=o.Buildings.Where(b=>b.Owner==o.Owner&&b.SiteId==home.Site.Id).ToArray();
                if(home.Site.Slots.Any(s=>!local.Any(b=>b.SlotId==s.Id)))continue;
                var target=!o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory)?PlayableBuildingKind.Factory:
                    !local.Any(b=>b.Kind==PlayableBuildingKind.Refinery)?PlayableBuildingKind.Refinery:(PlayableBuildingKind?)null;
                if(!target.HasValue)continue;
                foreach(var b in local.Where(b=>b.SlotId>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle!=null).OrderBy(b=>b.Id))
                {
                    // E5 converts redundant idle infrastructure only. Never centers/income,
                    // last production, live research, committed queues, repair or combat assets.
                    bool redundantScience=b.Kind==PlayableBuildingKind.ScientificCenter&&local.Count(x=>x.Kind==b.Kind)>1&&b.PrivateState.Research.All(r=>r.Complete);
                    bool redundantFactory=b.Kind==PlayableBuildingKind.Factory&&o.Buildings.Count(x=>x.Owner==o.Owner&&x.Kind==b.Kind&&x.Phase==ConstructionPhase.Ready&&x.PrivateState?.Lifecycle?.Selling!=true)>1&&
                        capacity!=null&&capacity.MeasuredTicks>=AiProfile.SecondsToTicks(ai.Value("economy.incomeWindowSeconds"),30)&&capacity.SettledIncomePerSecond<=capacity.SustainedSpendPerSecond-PlayableUnitRules.Cost(p,PlayableEntityKind.Tank)/PlayableUnitRules.Duration(p,PlayableEntityKind.Tank);
                    if((!redundantScience&&!redundantFactory)||b.Kind==target||b.PrivateState.QueueCount>0||b.PrivateState.Repeat||b.PrivateState.Lifecycle.Repairing||Danger(o,p,b))continue;
                    var a=Action(o,PlayableCommandKind.SellBuilding,b.Id,target.Value,home.Site.Id,b.SlotId,home.CenterId);
                    result.Add(new AiEconomyCandidate("infrastructure",a,AiEconomyAdmission.Reject(o,p,a),target==PlayableBuildingKind.Factory?1:0));
                }
            }
            bool science=scienceGoal!=null&&!o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.ScientificCenter&&
                b.PrivateState?.Lifecycle?.Selling!=true&&(b.Health>0||b.Phase!=ConstructionPhase.Ready));
            bool secondLine=capacity?.CanScale==true&&o.Buildings.Count(b=>b.Owner==o.Owner&&
                (b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar)&&b.Health>0&&b.PrivateState?.Lifecycle?.Selling!=true)==1;
            if(!science&&!secondLine)return result.AsReadOnly();
            var targetKind=science?PlayableBuildingKind.ScientificCenter:PlayableBuildingKind.Factory;
            if(discretionaryCash<TerritoryRules.Cost(p,targetKind))return result.AsReadOnly();
            foreach(var b in o.Buildings.Where(b=>b.Kind==PlayableBuildingKind.Refinery).OrderBy(b=>b.RefineryUpgraded).ThenBy(b=>b.Id))
            {
                var parentSite=o.Sites.FirstOrDefault(s=>s.Site.Id==b.SiteId);
                if(parentSite==null)continue;
                var a=Action(o,PlayableCommandKind.SellBuilding,b.Id,targetKind,b.SiteId,b.SlotId,parentSite.CenterId);
                // Keep the projected science demand in the existing action identity.
                a=new PlayableAiAction(a.ActionId,a.PlayerId,a.ProfileId,a.ProfileRevision,a.Generation,a.SnapshotSequence,a.Kind,a.CopyEntityIds(),
                    siteId:a.SiteId,slotId:a.SlotId,buildingKind:a.BuildingKind,parentId:a.ParentId,unitKind:startupKind,
                    researchKind:scienceGoal?.Research??a.ResearchKind,seed:a.Seed,sourceIdentity:a.SourceIdentity);
                if(CanSellRefinery(o,p,ai,a,discretionaryCash))
                {
                    result.Add(new AiEconomyCandidate("infrastructure",a,null,1));
                    break;
                }
            }
            return result.AsReadOnly();
        }
        internal static bool CanSellRefinery(PlayableAiObservation o,PlayableProfile p,AiProfile ai,PlayableAiAction action,long freeCredits)
        {
            var b=o.Buildings.FirstOrDefault(x=>x.Id==action.EntityIds.SingleOrDefault()&&x.Owner==o.Owner);
            if(b?.Kind!=PlayableBuildingKind.Refinery||b.PrivateState?.Lifecycle==null||b.PrivateState.Lifecycle.Repairing||
                b.PrivateState.Upgrade?.Active==true||b.SystemsDisabled||Danger(o,p,b)||AiEconomyAdmission.Reject(o,p,action)!=null||
                !AiIncomeSiteSelection.NeedsBuildingTerritory(o))return false;
            if(!o.Buildings.Any(x=>x.Owner==o.Owner&&x.Kind==PlayableBuildingKind.Mine&&x.Health>0&&
                x.Phase==ConstructionPhase.Ready&&!x.SystemsDisabled&&x.PrivateState?.Lifecycle?.Selling!=true))return false;
            var target=o.Sites.FirstOrDefault(s=>s.Site.Id==b.SiteId&&s.Owner==o.Owner&&s.Ready&&!s.Contested&&s.CenterId==action.ParentId);
            if(target==null||b.SlotId!=action.SlotId||b.SiteId!=action.SiteId||!target.Site.Slots.Any(s=>s.Id==b.SlotId)||
                !o.Buildings.Any(x=>x.Id==action.ParentId&&x.Owner==o.Owner&&x.Health>0&&x.Phase==ConstructionPhase.Ready&&x.PrivateState?.Lifecycle?.Selling!=true))return false;
            bool science=action.BuildingKind==PlayableBuildingKind.ScientificCenter;
            long price=TerritoryRules.Cost(p,action.BuildingKind);
            if(!science)price+=(long)ai.Value("economy.factoryLaunchCycles")*PlayableUnitRules.Cost(p,action.UnitKind);
            if(freeCredits<price)return false;
            if(science&&(!TerritoryRules.HasScienceProducer(o.Buildings,o.Owner)||
                o.Entities.Count(u=>u.Owner==o.Owner&&u.Health>0)<ai.Value("technology.minimumUnitsForScienceCenter")))return false;
            if(AiEconomicInvestmentPlanner.MixRejection(o,p,ai,action.BuildingKind,b.SiteId,b.Id,action.UnitKind)!=null)return false;
            var existingScience=o.Buildings.Where(x=>x.Owner==o.Owner&&x.Kind==PlayableBuildingKind.ScientificCenter&&
                x.PrivateState?.Lifecycle?.Selling!=true&&(x.Health>0||x.Phase!=ConstructionPhase.Ready));
            if(existingScience.Any(x=>AiEconomicInvestmentPlanner.MixRejection(o,p,ai,x.Kind,x.SiteId,b.Id)!=null))return false;
            double addedSpend=science?PlayableResearchRules.Cost(p,action.ResearchKind)/PlayableResearchRules.Duration(p,action.ResearchKind):
                AiEconomicInvestmentPlanner.UnitSpend(p,action.UnitKind);
            double lost=b.RefineryUpgraded?p.RefineryUpgradedIncome:p.RefineryIncomePerPeriod;
            double after=AiProfile.UsesExpertPolicy(o.Difficulty)?
                o.IncomePerSecond-lost/p.IncomePeriodSeconds*ai.IncomeMultiplier(o.Difficulty):
                AiEconomicInvestmentPlanner.SustainableIncome(o,p,ai)-lost/p.IncomePeriodSeconds;
            return AiEconomicInvestmentPlanner.CoversSpend(after,AiEconomicInvestmentPlanner.QueueSpend(o,p,action.UnitKind)+addedSpend,ai);
        }
        internal PlayableAiAction Admit(PlayableAiObservation o,PlayableAiAction a,AiBudgetLedger b)
        {
            if(pendingId!=0)throw new InvalidOperationException("Infrastructure pending.");
            if(a.Kind==PlayableCommandKind.SellBuilding)
            {
                var building=o.Buildings.Single(x=>x.Id==a.EntityIds.Single());site=a.SiteId;slot=a.SlotId;parent=a.ParentId;victim=building.Id;source=building.Kind;replacement=a.BuildingKind;
                created=o.Tick;expires=checked(o.Tick+b.ReservationLifetimeTicks);settled=false;reason="conversion selected; awaiting sale receipt";
            }
            pendingId=nextId++;pendingGeneration=o.Generation;pendingKind=a.Kind;
            return new PlayableAiAction(pendingId,a.PlayerId,a.ProfileId,a.ProfileRevision,a.Generation,a.SnapshotSequence,a.Kind,a.CopyEntityIds(),a.Target,a.SiteId,a.SlotId,a.BuildingKind,a.ParentId,a.TargetId,a.ProductionOrderId,a.UnitKind,a.ResearchKind,a.Seed,a.SourceIdentity);
        }
        internal void ObserveReceipt(PlayableAiTraceRecord r)
        {
            if(r.ActionId!=pendingId||r.OwnerId!=ownerId||r.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||r.ReceiptIdentity?.Generation!=pendingGeneration||r.ReceiptIdentity.OwnerId!=ownerId||r.Kind!=pendingKind||r.Status==PlayableAiDeliveryStatus.Accepted||r.Status==PlayableAiDeliveryStatus.Scheduled)return;
            pendingId=0;pendingGeneration=0;
            if(r.Kind==PlayableCommandKind.SellBuilding)
            {if(r.Status==PlayableAiDeliveryStatus.Applied){settled=true;reason="sale settled; waiting for physical slot";}else Release("sale rejected/cancelled");}
            else if(r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied)reason="replacement paid; conversion owns slot until bounded expiry";
        }
        internal void Release(string why){site=slot=parent=victim=0;created=expires=0;settled=false;reason=why;}
        internal void Use(AiIntent i,AiBudgetLedger b){if(i.ReservationId==HoldId)b.Release(HoldId);}
        internal void RetargetDefenseConversion(PlayableAiObservation o,PlayableProfile p,AiBudgetLedger b,AiProfile ai,AiKnowledgeState knowledge)
        {
            if(site==0||!settled||pendingId!=0||replacement!=PlayableBuildingKind.GaussTower||
                o.Buildings.Any(x=>x.SiteId==site&&x.SlotId==slot&&x.Kind==replacement)||
                !AiPositionDevelopmentPlanner.PreferSentry(o,p,ai,knowledge,site))return;
            b.Release(HoldId);
            replacement=PlayableBuildingKind.SentryTurret;
            reason="restore ordinary turret after changed threat";
        }
        internal void Observe(PlayableAiObservation o,PlayableProfile p,AiBudgetLedger b)
        {
            if(site!=0&&(o.Tick>=expires||!o.Sites.Any(s=>s.Site.Id==site&&s.Owner==o.Owner&&!s.Contested)||!o.Buildings.Any(x=>x.Id==parent&&x.Owner==o.Owner&&x.Health>0&&x.PrivateState?.Lifecycle?.Selling!=true)))
            {b.Release(HoldId);Release("conversion expired/site or parent lost");}
            if(site!=0&&settled&&pendingId==0&&!o.Buildings.Any(x=>x.SiteId==site&&x.SlotId==slot&&x.Kind==replacement))
            {
                b.Release(HoldId);
                var a=Action(o,PlayableCommandKind.BuildAt,target:replacement,siteId:site,slotId:slot,parentId:parent);
                int amount=(int)Math.Min(b.Available,TerritoryRules.Cost(p,replacement));
                b.Reserve(new AiBudgetEntry(HoldId,ConversionPurpose,a,amount,1,created,Math.Min(expires,created+b.ReservationLifetimeTicks),"physical replacement / loss / expiry",new[]{"slot:"+site+":"+slot},Terms(p)));
            }
            // Human-initiated repairs still protect their observed remaining money. They
            // do not become AI receipts or invented historical debits.
            foreach(var e in b.Capture().Reserved.Where(e=>e.Purpose==ExternalRepairPurpose))b.Release(e.Id);
            foreach(var building in o.Buildings.Where(x=>x.Owner==o.Owner&&x.PrivateState?.Lifecycle?.Repairing==true))
            {
                if(b.AwaitingPayments.Any(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair&&e.Action.EntityIds.Contains(building.Id)))continue;
                var a=Action(o,PlayableCommandKind.StartBuildingRepair,building.Id);
                b.Reserve(new AiBudgetEntry(ExternalRepairPurpose+":"+building.Id,ExternalRepairPurpose,a,(int)Math.Min(b.Available,Math.Ceiling(building.PrivateState.Lifecycle.RepairAllocation)),2,o.Tick,o.Tick+b.ReservationLifetimeTicks,"repair completion / cancellation / loss",System.Array.Empty<string>(),Terms(p)));
            }
        }
        internal bool IsConversion(AiBudgetEntry e)=>e.Purpose==ConversionPurpose;
        internal int Reprice(AiBudgetEntry e,PlayableProfile p)=>(int)Math.Min(e.Amount,TerritoryRules.Cost(p,replacement));
        internal void Rebind(PlayableProfile p,AiBudgetLedger b,long tick)
        {
            if(site==0)return;
            expires=Math.Min(expires,created+b.ReservationLifetimeTicks);
            var held=b.ReservationEntry(HoldId);b.Release(HoldId);
            if(tick>=expires){Release("profile horizon expired");return;}
            if(held==null)return;
            var old=held.Action;
            var action=new PlayableAiAction(old.ActionId,old.PlayerId,p.ProfileId,p.Revision,old.Generation,old.SnapshotSequence,old.Kind,old.CopyEntityIds(),old.Target,old.SiteId,old.SlotId,old.BuildingKind,old.ParentId,old.TargetId,old.ProductionOrderId,old.UnitKind,old.ResearchKind,old.Seed,old.SourceIdentity);
            int amount=(int)Math.Min(b.Available,Math.Min(held.Amount,TerritoryRules.Cost(p,replacement)));
            b.Reserve(new AiBudgetEntry(HoldId,ConversionPurpose,action,amount,1,created,expires,"physical replacement / loss / expiry",new[]{"slot:"+site+":"+slot},Terms(p)));
        }
        internal void ValidateFunding(AiBudgetLedger b,IReadOnlyList<AiIntent> pending,PlayableProfile p,long tick)
        {
            AiStateWire.Require(b.OwnerId==ownerId&&created<=tick&&expires-created<=b.ReservationLifetimeTicks,"infrastructure owner/horizon");
            bool Target(PlayableAiAction a)=>site>0&&settled&&a.Kind==PlayableCommandKind.BuildAt&&a.PlayerId==ownerId&&a.Generation==b.Generation&&a.SiteId==site&&a.SlotId==slot&&a.ParentId==parent&&a.BuildingKind==replacement&&a.EntityIds.Count==0;
            var ledger=b.Capture();
            var held=ledger.Reserved.Where(e=>e.Purpose==ConversionPurpose).ToArray();
            AiStateWire.Require(held.Length<=1&&held.All(e=>e.Id==HoldId&&Target(e.Action)&&e.Terms==Terms(p)&&e.CreatedTick==created&&e.ExpiresTick==expires&&
                (e.Amount>=0&&e.Amount<=TerritoryRules.Cost(p,replacement))&&e.Claims.SequenceEqual(new[]{"slot:"+site+":"+slot})),"conversion reservation binding");
            bool ValidStartup(AiIntent intent)
            {
                if(intent.StartupFund==0)return true;
                string id="factory-startup:"+site+":"+slot;
                return intent.Action.BuildingKind==PlayableBuildingKind.Factory&&ledger.Reserved.Any(entry=>
                    entry.Id==id&&entry.Purpose=="factory-startup"&&Target(entry.Action)&&
                    entry.Action.UnitKind==intent.Action.UnitKind&&entry.Amount==intent.StartupFund&&entry.Terms==Terms(p));
            }
            AiStateWire.Require(pending.Where(i=>i.Policy=="infrastructure"&&i.Action.Kind==PlayableCommandKind.BuildAt).All(i=>i.ReservationId==HoldId&&Target(i.Action)&&ValidStartup(i)&&i.Credits==TerritoryRules.Cost(p,replacement)&&i.Claims.Contains("slot:"+site+":"+slot)&&
                ledger.Unpaid.Any(e=>e.Id==i.Id&&e.Purpose=="infrastructure"&&Target(e.Action)&&e.Amount==i.Credits&&e.Claims.SequenceEqual(i.Claims)&&e.Terms==Terms(p))),"conversion pending payment binding");
            AiStateWire.Require(pending.Where(i=>i.Policy=="infrastructure").All(i=>i.Action.PlayerId==ownerId&&i.Action.Generation==b.Generation&&i.Action.Kind==pendingKind&&
                (i.Action.Kind!=PlayableCommandKind.SellBuilding||i.Action.SiteId==site&&i.Action.SlotId==slot&&i.Action.ParentId==parent&&i.Action.EntityIds.SequenceEqual(new[]{victim}))),"infrastructure pending binding");
        }
        internal void WriteState(BinaryWriter w){w.Write(nextId);w.Write(pendingId);w.Write(pendingGeneration);w.Write((int)pendingKind);w.Write(site);w.Write(slot);w.Write(parent);w.Write(victim);w.Write((int)source);w.Write((int)replacement);w.Write(created);w.Write(expires);w.Write(settled);String(w,reason);}
        internal void ReadState(BinaryReader r,long tick,long generation)
        {
            nextId=r.ReadInt64();pendingId=r.ReadInt64();pendingGeneration=r.ReadInt64();pendingKind=EnumValue<PlayableCommandKind>(r);site=r.ReadInt32();slot=r.ReadInt32();parent=r.ReadInt32();victim=r.ReadInt32();source=EnumValue<PlayableBuildingKind>(r);replacement=EnumValue<PlayableBuildingKind>(r);created=r.ReadInt64();expires=r.ReadInt64();settled=Boolean(r);reason=String(r);
            AiStateWire.Require(nextId>0&&pendingId>=0&&pendingId<nextId&&(pendingId==0?pendingGeneration==0:pendingGeneration==generation)&&site>=0&&slot>=0&&parent>=0&&victim>=0&&created>=0&&created<=tick&&expires>=created&&reason!=null&&(site==0?slot==0&&parent==0&&victim==0&&!settled:slot>0&&parent>0&&victim>0&&(source!=replacement||settled&&source==PlayableBuildingKind.SentryTurret)),"infrastructure state");
        }
    }
}

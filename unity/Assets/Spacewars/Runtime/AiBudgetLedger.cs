using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Detached, immutable terms. Paid entries are historical settlements, not claims on the bank.
    public sealed class AiBudgetEntry
    {
        private readonly string[] claims;
        public AiBudgetEntry(string id,string purpose,PlayableAiAction action,int amount,int priority,long createdTick,
            long expiresTick,string releaseCondition,IEnumerable<string> claims,string terms,AiReceiptIdentity receipt=null,bool awaitingPayment=false,double exactSettled=0)
        {
            if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(purpose)||action==null||amount<0||createdTick<0||expiresTick<createdTick||
                string.IsNullOrWhiteSpace(releaseCondition)||string.IsNullOrWhiteSpace(terms))throw new ArgumentException("Invalid budget entry.");
            this.claims=(claims??Array.Empty<string>()).ToArray();
            if(this.claims.Any(string.IsNullOrWhiteSpace)||this.claims.Distinct(StringComparer.Ordinal).Count()!=this.claims.Length)throw new ArgumentException("Invalid budget claims.");
            Array.Sort(this.claims,StringComparer.Ordinal);
            Id=id;Purpose=purpose;Action=action;Amount=amount;Priority=priority;CreatedTick=createdTick;ExpiresTick=expiresTick;
            if(awaitingPayment&&receipt==null)throw new ArgumentException("Payment requires accepted receipt.");
            if(double.IsNaN(exactSettled)||double.IsInfinity(exactSettled)||exactSettled<0)throw new ArgumentException("Invalid exact settlement.");
            ExactSettled=exactSettled;ReleaseCondition=releaseCondition;Terms=terms;Receipt=receipt;AwaitingPayment=awaitingPayment;
        }
        public string Id{get;} public string Purpose{get;} public PlayableAiAction Action{get;} public int Amount{get;}
        public int Priority{get;} public long CreatedTick{get;} public long ExpiresTick{get;} public string ReleaseCondition{get;}
        public double ExactSettled{get;}
        public string Terms{get;} public AiReceiptIdentity Receipt{get;} public bool AwaitingPayment{get;} public IReadOnlyList<string> Claims=>Array.AsReadOnly(claims);
        internal AiBudgetEntry With(int amount,string terms,AiReceiptIdentity receipt=null,bool awaitingPayment=false,double exactSettled=0)=>new AiBudgetEntry(Id,Purpose,Action,amount,Priority,CreatedTick,ExpiresTick,ReleaseCondition,claims,terms,receipt,awaitingPayment,exactSettled==0?ExactSettled:exactSettled);
    }

    // DTO deliberately excludes callbacks/world references. Collections are defensive copies.
    public sealed class AiBudgetState
    {
        private readonly AiBudgetEntry[] reserved,unpaid,paid;
        public AiBudgetState(string ownerId,long generation,int liquid,int safetyReserve,long paidTotal,
            IEnumerable<AiBudgetEntry> reserved,IEnumerable<AiBudgetEntry> unpaid,IEnumerable<AiBudgetEntry> paid,long reservationLifetimeTicks=0,long admittedDecision=0,int admittedAction=0,double repairPaidTotal=0)
        {
            OwnerId=ownerId;Generation=generation;Liquid=liquid;SafetyReserve=safetyReserve;PaidTotal=paidTotal;
            this.reserved=reserved.ToArray();this.unpaid=unpaid.ToArray();this.paid=paid.ToArray();
            ReservationLifetimeTicks=reservationLifetimeTicks==0?AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.reservationExpirySeconds"),30):reservationLifetimeTicks;
            AdmittedDecision=admittedDecision;AdmittedAction=admittedAction;RepairPaidTotal=repairPaidTotal;
        }
        public string OwnerId{get;} public long Generation{get;} public int Liquid{get;} public int SafetyReserve{get;} public long PaidTotal{get;}
        public double RepairPaidTotal{get;}
        public long ReservationLifetimeTicks{get;}
        public long AdmittedDecision{get;} public int AdmittedAction{get;}
        public IReadOnlyList<AiBudgetEntry> Reserved=>Array.AsReadOnly(reserved);
        public IReadOnlyList<AiBudgetEntry> Unpaid=>Array.AsReadOnly(unpaid);
        public IReadOnlyList<AiBudgetEntry> Paid=>Array.AsReadOnly(paid);
    }

    public sealed class AiBudgetLedger
    {
        private readonly Dictionary<string,AiBudgetEntry> reserved=new Dictionary<string,AiBudgetEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string,AiBudgetEntry> unpaid=new Dictionary<string,AiBudgetEntry>(StringComparer.Ordinal);
        // Bounded diagnostic history; the cumulative settled total remains durable. Old receipts
        // cannot affect a new attempt because reconciliation requires its exact active receipt ID.
        private readonly List<AiBudgetEntry> paid=new List<AiBudgetEntry>();
        private long admittedDecision;private int admittedAction;
        public AiBudgetLedger(string ownerId,long generation,long reservationLifetimeTicks=0)
        {if(string.IsNullOrWhiteSpace(ownerId)||generation<1||reservationLifetimeTicks<0)throw new ArgumentException("Invalid budget owner.");OwnerId=ownerId;Generation=generation;
            ReservationLifetimeTicks=reservationLifetimeTicks==0?AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.reservationExpirySeconds"),30):reservationLifetimeTicks;}
        public string OwnerId{get;} public long Generation{get;} public int Liquid{get;private set;}
        public double RepairPaidTotal{get;private set;}
        public int SafetyReserve{get;private set;} public long PaidTotal{get;private set;}
        public long ReservationLifetimeTicks{get;private set;}
        public long Reserved=>reserved.Values.Sum(x=>(long)x.Amount);
        public long Unpaid=>unpaid.Values.Sum(x=>(long)x.Amount);
        public long Available=>Math.Max(0,(long)Liquid-SafetyReserve-Reserved-Unpaid);
        public IReadOnlyList<string> ActiveClaims=>Array.AsReadOnly(reserved.Values.Concat(unpaid.Values).SelectMany(x=>x.Claims).OrderBy(x=>x,StringComparer.Ordinal).ToArray());
        public void ObserveBank(int liquid){if(liquid<0)throw new ArgumentException("Negative bank.");Liquid=liquid;}
        public void SetSafetyReserve(int amount){if(amount<0)throw new ArgumentException("Negative safety reserve.");SafetyReserve=amount;}
        public long ReservationAmount(string intentId)=>reserved.TryGetValue(intentId,out var entry)?entry.Amount:0;
        internal AiBudgetEntry ReservationEntry(string intentId)=>reserved.TryGetValue(intentId,out var entry)?entry:null;
        public bool HasUnpaid(string intentId)=>unpaid.ContainsKey(intentId);
        internal AiBudgetEntry[] AwaitingPayments=>unpaid.Values.Where(x=>x.AwaitingPayment).ToArray();
        public bool HasConflictingClaims(string intentId,IEnumerable<string> claims,string replacingReservation=null)=>reserved.Values.Concat(unpaid.Values).Where(x=>x.Id!=intentId&&x.Id!=replacingReservation).SelectMany(x=>x.Claims).Intersect(claims,StringComparer.Ordinal).Any();
        public void RebindLifetime(long ticks)
        {
            if(ticks<1)throw new ArgumentException("Invalid reservation horizon.");ReservationLifetimeTicks=ticks;
            foreach(var x in reserved.Values.ToArray())if(x.ExpiresTick-x.CreatedTick>ticks)
                reserved[x.Id]=new AiBudgetEntry(x.Id,x.Purpose,x.Action,x.Amount,x.Priority,x.CreatedTick,checked(x.CreatedTick+ticks),x.ReleaseCondition,x.Claims,x.Terms);
        }
        public bool Reserve(AiBudgetEntry entry)
        {
            ValidateEntry(entry,false);
            if(entry.ExpiresTick-entry.CreatedTick>ReservationLifetimeTicks)throw new ArgumentException("Unbounded reservation.");
            if(reserved.TryGetValue(entry.Id,out var existing))return Same(existing,entry);
            if(unpaid.ContainsKey(entry.Id)||!Fits(entry,null))return false;
            reserved.Add(entry.Id,entry);return true;
        }
        public bool Accept(AiBudgetEntry entry)
        {
            ValidateEntry(entry,true);
            if(unpaid.TryGetValue(entry.Id,out var existing))return Same(existing,entry);
            if(entry.AwaitingPayment||entry.Receipt.DecisionOrdinal<admittedDecision||entry.Receipt.DecisionOrdinal==admittedDecision&&entry.Receipt.ActionOrdinal<=admittedAction)return false;
            if(unpaid.Values.Concat(paid).Any(x=>x.Receipt.Id==entry.Receipt.Id))return false;
            if(!Fits(entry,entry.Id))return false;
            reserved.Remove(entry.Id);unpaid.Add(entry.Id,entry);admittedDecision=entry.Receipt.DecisionOrdinal;admittedAction=entry.Receipt.ActionOrdinal;return true;
        }
        private bool Fits(AiBudgetEntry entry,string replacing)
        {
            var active=reserved.Values.Concat(unpaid.Values).Where(x=>x.Id!=replacing).ToArray();
            return entry.Amount<=Math.Max(0,(long)Liquid-SafetyReserve-active.Sum(x=>(long)x.Amount))&&
                !active.SelectMany(x=>x.Claims).Intersect(entry.Claims,StringComparer.Ordinal).Any();
        }
        // Releasing an unpaid entry requires a terminal receipt. An expiry/cancel must still
        // reach the ordinary owner-loop terminal path so policy pending state stays consistent.
        public bool Release(string id)=>reserved.Remove(id);
        public void ReleaseAllReservations()=>reserved.Clear();
        public void Expire(long tick,Func<AiBudgetEntry,bool> stillValid=null)
        {
            if(tick<0)throw new ArgumentException("Negative tick.");
            foreach(var entry in reserved.Values.Where(x=>tick>x.ExpiresTick||stillValid!=null&&!stillValid(x)).ToArray())reserved.Remove(entry.Id);
        }
        public bool Reconcile(string intentId,AiReceiptIdentity receipt,PlayableAiDeliveryStatus status,bool gameplayPaid=true)
        {
            if(receipt==null||receipt.Generation!=Generation||receipt.OwnerId!=OwnerId||status==PlayableAiDeliveryStatus.Scheduled||status==PlayableAiDeliveryStatus.Accepted)return false;
            if(!unpaid.TryGetValue(intentId,out var entry)||entry.Receipt.Id!=receipt.Id||entry.AwaitingPayment)return false;
            if(!Enum.IsDefined(typeof(PlayableAiDeliveryStatus),status))throw new ArgumentException("Invalid terminal status.");
            if(status==PlayableAiDeliveryStatus.Applied&&!gameplayPaid){unpaid[intentId]=entry.Action.Kind==PlayableCommandKind.StartBuildingRepair?
                new AiBudgetEntry(entry.Id,entry.Purpose,entry.Action,entry.Amount,entry.Priority,entry.CreatedTick,entry.ExpiresTick,entry.ReleaseCondition,entry.Claims.Where(c=>!c.StartsWith("policy:")&&!c.StartsWith("recipient:")),entry.Terms,receipt,true):entry.With(entry.Amount,entry.Terms,receipt,true);return true;}
            unpaid.Remove(intentId);
            if(status==PlayableAiDeliveryStatus.Applied)
            {PaidTotal=checked(PaidTotal+entry.Amount);paid.Add(entry);if(paid.Count>128)paid.RemoveAt(0);}
            return true;
        }
        public bool SettlePayment(string intentId,AiReceiptIdentity receipt,bool exists,int paidAmount,int futureAmount,string terms,bool isPaid=false)
        {
            if(receipt==null||!unpaid.TryGetValue(intentId,out var entry)||entry.Receipt.Id!=receipt.Id||!entry.AwaitingPayment)return false;
            if(paidAmount<0||futureAmount<0||string.IsNullOrWhiteSpace(terms))throw new ArgumentException("Invalid settlement.");
            if(!exists){unpaid.Remove(intentId);return true;}
            if(paidAmount==0&&!isPaid){unpaid[intentId]=entry.With(futureAmount,terms,receipt,true);return true;}
            unpaid.Remove(intentId);var settled=entry.With(paidAmount,terms,receipt);
            PaidTotal=checked(PaidTotal+paidAmount);paid.Add(settled);if(paid.Count>128)paid.RemoveAt(0);return true;
        }
        internal void SettleRepair(AiBudgetEntry entry,double cumulative,double remaining,bool active)
        {
            if(!unpaid.TryGetValue(entry.Id,out var current)||!current.AwaitingPayment||current.Action.Kind!=PlayableCommandKind.StartBuildingRepair||current.Receipt.Id!=entry.Receipt.Id)return;
            if(cumulative+1e-9<current.ExactSettled||remaining<0||double.IsNaN(cumulative)||double.IsInfinity(cumulative))throw new InvalidOperationException("Invalid repair settlement.");
            RepairPaidTotal+=Math.Max(0,cumulative-current.ExactSettled);
            if(active)unpaid[entry.Id]=current.With((int)Math.Ceiling(remaining),current.Terms,current.Receipt,true,cumulative);
            else {unpaid.Remove(entry.Id);paid.Add(current.With(0,current.Terms,current.Receipt,false,cumulative));if(paid.Count>128)paid.RemoveAt(0);}
        }
        // Future reservations use new catalog prices. Already paid terms and accepted attempts
        // remain immutable; owner profile barriers cancel obsolete unpaid commands first.
        public void RepriceReservations(Func<AiBudgetEntry,int> price,string terms)
        {
            if(price==null||string.IsNullOrWhiteSpace(terms))throw new ArgumentException("Invalid repricing.");
            var updated=reserved.Values.OrderByDescending(x=>x.Priority).ThenBy(x=>x.CreatedTick).ThenBy(x=>x.Id,StringComparer.Ordinal)
                .Select(x=>x.With(price(x),terms)).ToArray();
            reserved.Clear();foreach(var entry in updated)if(Fits(entry,null))reserved.Add(entry.Id,entry);
        }
        public AiBudgetState Capture()=>new AiBudgetState(OwnerId,Generation,Liquid,SafetyReserve,PaidTotal,
            reserved.Values.OrderBy(x=>x.Id,StringComparer.Ordinal),unpaid.Values.OrderBy(x=>x.Id,StringComparer.Ordinal),paid,ReservationLifetimeTicks,admittedDecision,admittedAction,RepairPaidTotal);
        public static AiBudgetLedger Restore(AiBudgetState state,long tick)
        {
            if(state==null||tick<0||state.Liquid<0||state.SafetyReserve<0||state.PaidTotal<0||double.IsNaN(state.RepairPaidTotal)||double.IsInfinity(state.RepairPaidTotal)||state.RepairPaidTotal<0||state.Paid.Count>128||state.ReservationLifetimeTicks<1||state.AdmittedDecision<0||state.AdmittedAction<0||(state.AdmittedDecision==0)!=(state.AdmittedAction==0))throw new ArgumentException("Invalid budget state.");
            var ledger=new AiBudgetLedger(state.OwnerId,state.Generation,state.ReservationLifetimeTicks){Liquid=state.Liquid,SafetyReserve=state.SafetyReserve,PaidTotal=state.PaidTotal,RepairPaidTotal=state.RepairPaidTotal,admittedDecision=state.AdmittedDecision,admittedAction=state.AdmittedAction};
            foreach(var entry in state.Reserved){ledger.ValidateEntry(entry,false);if(entry.CreatedTick>tick||entry.ExpiresTick-entry.CreatedTick>ledger.ReservationLifetimeTicks||!ledger.reserved.TryAdd(entry.Id,entry))throw new ArgumentException("Invalid reserved state.");}
            foreach(var entry in state.Unpaid){ledger.ValidateEntry(entry,true);if(entry.CreatedTick>tick||ledger.reserved.ContainsKey(entry.Id)||!ledger.unpaid.TryAdd(entry.Id,entry))throw new ArgumentException("Invalid unpaid state.");}
            foreach(var entry in state.Paid){ledger.ValidateEntry(entry,true);if(entry.CreatedTick>tick||entry.AwaitingPayment)throw new ArgumentException("Invalid paid clock/state.");ledger.paid.Add(entry);}
            var claims=ledger.ActiveClaims;
            var receipts=state.Unpaid.Concat(state.Paid).Select(x=>x.Receipt.Id).ToArray();
            if(claims.Distinct(StringComparer.Ordinal).Count()!=claims.Count||receipts.Distinct(StringComparer.Ordinal).Count()!=receipts.Length||state.Paid.Sum(x=>(long)x.Amount)>state.PaidTotal||state.Paid.Concat(state.Unpaid).Sum(x=>x.ExactSettled)>state.RepairPaidTotal+1e-7||
                state.Unpaid.Concat(state.Paid).Any(x=>x.Receipt.DecisionOrdinal>state.AdmittedDecision||x.Receipt.DecisionOrdinal==state.AdmittedDecision&&x.Receipt.ActionOrdinal>state.AdmittedAction))throw new ArgumentException("Overlapping budget state.");
            return ledger;
        }
        private void ValidateEntry(AiBudgetEntry entry,bool accepted)
        {
            if(entry==null||entry.ExactSettled>0&&entry.Action.Kind!=PlayableCommandKind.StartBuildingRepair||entry.Action.PlayerId!=OwnerId||entry.Action.Generation!=Generation||
                (entry.Receipt!=null)!=accepted||accepted&&(entry.Receipt.OwnerId!=OwnerId||entry.Receipt.Generation!=Generation)||entry.AwaitingPayment&&entry.Action.Kind!=PlayableCommandKind.QueueResearch&&entry.Action.Kind!=PlayableCommandKind.StartBuildingRepair)throw new ArgumentException("Foreign budget entry.");
        }
        private static bool Same(AiBudgetEntry a,AiBudgetEntry b)=>a.Id==b.Id&&a.Purpose==b.Purpose&&a.Action.Kind==b.Action.Kind&&
            a.Amount==b.Amount&&a.Priority==b.Priority&&a.CreatedTick==b.CreatedTick&&a.ExpiresTick==b.ExpiresTick&&a.ReleaseCondition==b.ReleaseCondition&&a.Terms==b.Terms&&
            a.Action.SiteId==b.Action.SiteId&&a.Action.SlotId==b.Action.SlotId&&a.Action.BuildingKind==b.Action.BuildingKind&&a.Action.UnitKind==b.Action.UnitKind&&a.Action.ResearchKind==b.Action.ResearchKind&&
            a.Claims.SequenceEqual(b.Claims)&&(a.Receipt?.Id==b.Receipt?.Id)&&a.AwaitingPayment==b.AwaitingPayment&&a.ExactSettled==b.ExactSettled&&
            WorldWire.Pack(w=>AiStateWire.Write(w,a.Action)).SequenceEqual(WorldWire.Pack(w=>AiStateWire.Write(w,b.Action)));
    }
    internal sealed partial class PlayableDomain
    {
        internal int AiLiquidCredits(string ownerId)=>(int)Math.Floor(Balance(OwnerFor(ownerId)));
        internal void ReconcileAiPayments(AiBudgetLedger ledger)
        {
            var owner=OwnerFor(ledger.OwnerId);
            foreach(var entry in ledger.AwaitingPayments)
            {
                if(entry.Action.Kind==PlayableCommandKind.StartBuildingRepair)
                {
                    bool active=buildings.TryGetValue(entry.Action.EntityIds.Single(),out var b)&&b.Owner==owner&&b.Repair!=null&&b.Repair.AllocationReceipt==entry.Receipt.Id;
                    var r=active?b.Repair:null;
                    double paid=r==null?entry.ExactSettled:r.TotalCost*Math.Min(1,r.PaidSeconds/r.Duration);
                    ledger.SettleRepair(entry,paid,r==null?0:Math.Max(0,r.TotalCost-paid),active);continue;
                }
                if(entry.Action.Kind!=PlayableCommandKind.QueueResearch)throw new InvalidOperationException("Unknown deferred payment.");
                var order=Research(owner).SingleOrDefault(x=>x.Kind==entry.Action.ResearchKind);
                ledger.SettlePayment(entry.Id,entry.Receipt,order!=null,(int)Math.Ceiling(order?.PaidCost??0),
                    (int)Math.Ceiling(ResearchCost(entry.Action.ResearchKind)),profile.ProfileId+"@"+profile.Revision,order!=null&&(order.Active||order.Complete));
            }
            ledger.ObserveBank(AiLiquidCredits(ledger.OwnerId));
        }
        internal void StepWithAiRepairAccounting(double dt,IEnumerable<AiBudgetLedger> ledgers)
        {
            var work=ledgers.SelectMany(l=>l.AwaitingPayments.Where(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair)
                .Select(e=>new {Ledger=l,Entry=e,Building=buildings.TryGetValue(e.Action.EntityIds.Single(),out var b)?b:null}))
                .Where(x=>x.Building?.Repair!=null&&x.Building.Repair.AllocationReceipt==x.Entry.Receipt.Id).Select(x=>new {x.Ledger,x.Entry,x.Building,Repair=x.Building.Repair}).ToArray();
            Step(dt);
            foreach(var x in work)
            {
                double cumulative=x.Repair.TotalCost*Math.Min(1,x.Repair.PaidSeconds/x.Repair.Duration);
                bool active=buildings.ContainsKey(x.Building.Id)&&ReferenceEquals(x.Building.Repair,x.Repair);
                x.Ledger.SettleRepair(x.Entry,cumulative,active?Math.Max(0,x.Repair.TotalCost-cumulative):0,active);
            }
        }
        internal void BindAiRepairPayment(PlayableAiAction action,AiReceiptIdentity identity)
        {if(action.Kind==PlayableCommandKind.StartBuildingRepair&&buildings.TryGetValue(action.EntityIds.Single(),out var b)&&b.Repair!=null)b.Repair.AllocationReceipt=identity.Id;}
        internal bool AiCommandPaid(PlayableAiAction action)
        {
            if(action.Kind==PlayableCommandKind.StartBuildingRepair)return false;
            if(action.Kind!=PlayableCommandKind.QueueResearch)return true;
            return Research(OwnerFor(action.PlayerId)).Any(x=>x.Kind==action.ResearchKind&&x.Active);
        }
        internal void ValidateAiBudget(AiBudgetLedger ledger)
        {
            var owner=OwnerFor(ledger.OwnerId);
            foreach(var entry in ledger.AwaitingPayments)
                AiStateWire.Require(entry.Action.Kind==PlayableCommandKind.StartBuildingRepair?buildings.TryGetValue(entry.Action.EntityIds.Single(),out var b)&&b.Owner==owner&&b.Repair!=null&&b.Repair.AllocationReceipt==entry.Receipt.Id&&entry.Terms==Terms(b.Repair.TermsRevision).ProfileId+"@"+b.Repair.TermsRevision&&Math.Abs(entry.ExactSettled-b.Repair.TotalCost*b.Repair.PaidSeconds/b.Repair.Duration)<1e-7&&entry.Amount==(int)Math.Ceiling(b.Repair.TotalCost-entry.ExactSettled):entry.Action.Kind==PlayableCommandKind.QueueResearch&&Research(owner).Any(x=>x.Kind==entry.Action.ResearchKind),"orphan deferred payment");
        }
    }
}

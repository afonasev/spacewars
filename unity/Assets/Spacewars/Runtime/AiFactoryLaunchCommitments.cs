using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using static Spacewars.Runtime.WorldWire;

namespace Spacewars.Runtime
{
    // An accepted factory owns a bounded launch fund, distinct from its paid building price.
    internal sealed class AiFactoryLaunchCommitments
    {
        private sealed class Launch
        {
            internal string Id,BuildIntent,BuildReceipt,PendingReceipt;
            internal PlayableAiAction Build;
            internal long Created,Expires;
            internal int Producer,Cycles;
            internal bool Applied;
        }
        private readonly List<Launch> launches=new List<Launch>();
        private static int Price(PlayableProfile p)=>PlayableUnitRules.Cost(p,PlayableEntityKind.Tank);
        internal string ReservationFor(PlayableAiAction a)=>launches.FirstOrDefault(x=>x.Producer>0&&a.EntityIds.Count==1&&a.EntityIds[0]==x.Producer&&a.Kind==PlayableCommandKind.QueueTank&&a.UnitKind==PlayableEntityKind.Tank)?.Id;
        internal bool ContainsProducer(int id)=>launches.Any(x=>x.Producer==id);
        internal bool Contains(string id)=>launches.Any(x=>x.Id==id);
        internal bool IsStartup(AiBudgetEntry e)=>e.Purpose=="factory-startup";
        internal int Reprice(AiBudgetEntry e,PlayableProfile p)=>checked(launches.Single(x=>x.Id==e.Id).Cycles*Price(p));
        internal void Begin(AiIntent intent,AiReceiptIdentity receipt,long tick,AiBudgetLedger budget,PlayableProfile p,AiProfile ai)
        {
            if(intent.StartupFund==0)return;
            var a=intent.Action;string id="factory-startup:"+a.SiteId+":"+a.SlotId;
            if(launches.Any(x=>x.Id==id))throw new InvalidOperationException("Duplicate factory launch commitment.");
            var x=new Launch{Id=id,BuildIntent=intent.Id,BuildReceipt=receipt.Id,Build=a,Created=tick,Expires=checked(tick+budget.ReservationLifetimeTicks),Cycles=(int)ai.Value("economy.factoryLaunchCycles")};
            if(!budget.Reserve(Entry(x,checked(x.Cycles*Price(p)),p)))throw new InvalidOperationException("Selected launch fund no longer fits.");
            launches.Add(x);launches.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));
        }
        private static PlayableAiAction Target(Launch x,PlayableProfile p)=>x.Producer==0?x.Build:new PlayableAiAction(x.Build.ActionId,x.Build.PlayerId,p.ProfileId,p.Revision,x.Build.Generation,x.Build.SnapshotSequence,PlayableCommandKind.QueueTank,new[]{x.Producer},unitKind:PlayableEntityKind.Tank,seed:x.Build.Seed,sourceIdentity:x.Build.SourceIdentity);
        private static AiBudgetEntry Entry(Launch x,int amount,PlayableProfile p)=>new AiBudgetEntry(x.Id,"factory-startup",Target(x,p),amount,1,x.Created,x.Expires,
            "new line paid cycle / factory destroyed or selling / expiry",new[]{x.Id},p.ProfileId+"@"+p.Revision);
        internal void Observe(PlayableAiObservation o,AiBudgetLedger budget,PlayableProfile p)
        {
            foreach(var x in launches.ToArray())
            {
                var producer=o.Buildings.FirstOrDefault(b=>b.Owner==o.Owner&&b.SiteId==x.Build.SiteId&&b.SlotId==x.Build.SlotId&&b.Kind==PlayableBuildingKind.Factory&&b.Health>0&&b.PrivateState?.Lifecycle?.Selling!=true);
                if(o.Tick>x.Expires||x.Applied&&producer==null||!o.Sites.Any(s=>s.Site.Id==x.Build.SiteId&&s.Owner==o.Owner))
                {Drop(x,budget);continue;}
                if(x.Applied&&producer!=null)
                {
                    if(x.Producer!=0&&x.Producer!=producer.Id){Drop(x,budget);continue;}
                    if(x.Producer==0)
                    {
                        x.Producer=producer.Id;var amount=budget.ReservationAmount(x.Id);budget.Release(x.Id);
                        if(amount>0&&!budget.Reserve(Entry(x,checked((int)amount),p))){Drop(x,budget);continue;}
                    }
                }
                // Missing funding after an external bank loss/rebind is a truthful cancelled
                // commitment. Never silently promise resources the ledger cannot protect.
                if(x.PendingReceipt==null&&budget.ReservationAmount(x.Id)!=checked(x.Cycles*Price(p)))Drop(x,budget);
            }
        }
        internal void Use(AiIntent intent,AiReceiptIdentity receipt,AiBudgetLedger budget,PlayableProfile p)
        {
            if(intent.ReservationId==null)return;
            var x=launches.Single(l=>l.Id==intent.ReservationId);
            var held=budget.ReservationAmount(x.Id);
            if(x.PendingReceipt!=null||held<intent.Credits||x.Producer==0||intent.Action.Kind!=PlayableCommandKind.QueueTank||!intent.Action.EntityIds.SequenceEqual(new[]{x.Producer}))throw new InvalidOperationException("Invalid startup admission.");
            budget.Release(x.Id);
            if(held>intent.Credits&&!budget.Reserve(Entry(x,checked((int)held-intent.Credits),p)))throw new InvalidOperationException("Startup remainder lost.");
            x.PendingReceipt=receipt.Id;
        }
        internal void Terminal(AiIntent intent,AiReceiptIdentity receipt,PlayableAiDeliveryStatus status,bool paid,AiBudgetLedger budget,PlayableProfile p)
        {
            foreach(var x in launches.ToArray())
            {
                if(x.BuildReceipt==receipt.Id)
                {
                    if(status==PlayableAiDeliveryStatus.Applied)x.Applied=true;
                    else Drop(x,budget);
                }
                else if(x.PendingReceipt==receipt.Id)
                {
                    x.PendingReceipt=null;
                    if(status==PlayableAiDeliveryStatus.Applied&&paid)x.Cycles--;
                    if(x.Cycles==0){Drop(x,budget);continue;}
                    budget.Release(x.Id);
                    if(!budget.Reserve(Entry(x,checked(x.Cycles*Price(p)),p)))Drop(x,budget);
                }
            }
        }
        internal void ReconcileFunding(AiBudgetLedger budget,PlayableProfile p,long tick)
        {foreach(var x in launches.ToArray())if(x.PendingReceipt==null&&(tick>x.Expires||budget.ReservationAmount(x.Id)!=checked(x.Cycles*Price(p))))Drop(x,budget);}
        internal void RebindHorizon(AiBudgetLedger budget)
        {foreach(var x in launches)x.Expires=Math.Min(x.Expires,checked(x.Created+budget.ReservationLifetimeTicks));}
        private void Drop(Launch x,AiBudgetLedger budget){budget.Release(x.Id);launches.Remove(x);}
        internal void Clear(AiBudgetLedger budget){foreach(var x in launches.ToArray())Drop(x,budget);}
        internal void Validate(AiBudgetLedger budget,IReadOnlyList<AiIntent> pending,IReadOnlyList<AiReceiptIdentity> receipts,PlayableProfile p,long tick)
        {
            foreach(var x in launches)
            {
                AiStateWire.Require(x.Created<=tick&&x.Expires-x.Created<=budget.ReservationLifetimeTicks&&x.Build.PlayerId==budget.OwnerId&&x.Build.Generation==budget.Generation,"startup binding");
                long expected=checked(x.Cycles*Price(p));
                if(x.PendingReceipt!=null)
                {
                    int i=Enumerable.Range(0,receipts.Count).Where(n=>receipts[n].Id==x.PendingReceipt).DefaultIfEmpty(-1).Single();
                    AiStateWire.Require(i>=0&&pending[i].ReservationId==x.Id&&pending[i].Action.EntityIds.Contains(x.Producer),"startup pending receipt");
                    expected-=pending[i].Credits;
                }
                AiStateWire.Require(expected>=0&&budget.ReservationAmount(x.Id)==expected,"startup ledger amount");
                var held=budget.ReservationEntry(x.Id);AiStateWire.Require(held==null||held.Purpose=="factory-startup"&&(x.Producer==0?held.Action.Kind==PlayableCommandKind.BuildAt&&held.Action.SiteId==x.Build.SiteId&&held.Action.SlotId==x.Build.SlotId:held.Action.Kind==PlayableCommandKind.QueueTank&&held.Action.EntityIds.SequenceEqual(new[]{x.Producer})),"startup reservation target");
                AiStateWire.Require(x.Applied||receipts.Any(r=>r.Id==x.BuildReceipt),"startup accepted build binding");
            }
            AiStateWire.Require(budget.Capture().Reserved.Where(IsStartup).All(e=>launches.Any(x=>x.Id==e.Id)),"orphan startup reservation");
            AiStateWire.Require(pending.Where(i=>i.ReservationId!=null&&i.Policy=="production").All(i=>launches.Any(x=>x.Id==i.ReservationId)),"orphan startup command");
        }
        internal void WriteState(BinaryWriter w)=>WorldWire.Array(w,launches.OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray(),x=>
        {String(w,x.Id);String(w,x.BuildIntent);String(w,x.BuildReceipt);String(w,x.PendingReceipt);AiStateWire.Write(w,x.Build);w.Write(x.Created);w.Write(x.Expires);w.Write(x.Producer);w.Write(x.Cycles);w.Write(x.Applied);});
        internal static AiFactoryLaunchCommitments ReadState(BinaryReader r,long tick)
        {
            var m=new AiFactoryLaunchCommitments();m.launches.AddRange(WorldWire.Array(r,()=>new Launch{Id=String(r),BuildIntent=String(r),BuildReceipt=String(r),PendingReceipt=String(r),Build=AiStateWire.ReadPlayableAiAction(r),Created=r.ReadInt64(),Expires=r.ReadInt64(),Producer=r.ReadInt32(),Cycles=r.ReadInt32(),Applied=Boolean(r)}));
            AiStateWire.Require(m.launches.Select(x=>x.Id).Distinct().Count()==m.launches.Count,"duplicate startup commitment");
            foreach(var x in m.launches)AiStateWire.Require(x.Build!=null&&x.Build.Kind==PlayableCommandKind.BuildAt&&x.Build.BuildingKind==PlayableBuildingKind.Factory&&x.Id=="factory-startup:"+x.Build.SiteId+":"+x.Build.SlotId&&!string.IsNullOrWhiteSpace(x.BuildIntent)&&!string.IsNullOrWhiteSpace(x.BuildReceipt)&&x.Created>=0&&x.Created<=tick&&x.Expires>=x.Created&&x.Producer>=0&&x.Cycles>0&&x.Cycles<=AiProfileMetadata.Field("economy.factoryLaunchCycles").Maximum&&(!x.Applied?x.Producer==0:true),"startup values");
            return m;
        }
    }
}

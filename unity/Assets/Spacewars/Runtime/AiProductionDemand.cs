using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using static Spacewars.Runtime.WorldWire;

namespace Spacewars.Runtime
{
    // Owner-only measurements. Observe is an authority checkpoint, never a proposal effect.
    public sealed class AiProductionDemand
    {
        private sealed class Sample
        {
            internal long Tick; internal double Income; internal int Bank,Lines,Busy;
        }
        private readonly List<Sample> samples=new List<Sample>();
        private readonly SortedDictionary<int,long> idleSince=new SortedDictionary<int,long>();
        private long window;
        public void Observe(PlayableAiObservation o,AiProfile ai)
        {
            window=AiProfile.SecondsToTicks(ai.Value("economy.incomeWindowSeconds"),30);
            if(samples.Count>0&&samples[samples.Count-1].Tick==o.Tick)return;
            if(samples.Count>0&&(samples[samples.Count-1].Tick>o.Tick||samples[samples.Count-1].Income>o.SettledIncome))throw new InvalidOperationException("Income history regressed.");
            var lines=Lines(o).ToArray();
            foreach(var id in idleSince.Keys.Where(id=>!lines.Any(b=>b.Id==id)).ToArray())idleSince.Remove(id);
            foreach(var b in lines)
            {
                if(b.PrivateState.QueueCount>0)idleSince.Remove(b.Id);
                else if(!idleSince.ContainsKey(b.Id))idleSince.Add(b.Id,o.Tick);
            }
            samples.Add(new Sample{Tick=o.Tick,Income=o.SettledIncome,Bank=o.Credits,Lines=lines.Length,Busy=lines.Count(Productive)});
            // Retain the boundary sample so the full interval's settlements remain exact.
            while(samples.Count>1&&samples[1].Tick<=o.Tick-window)samples.RemoveAt(0);
        }
        internal static IEnumerable<PlayableBuildingSnapshot> Lines(PlayableAiObservation o,AiRosterCatalog catalog=null,PlayableEntityKind productionKind=PlayableEntityKind.Tank)=>
            AiEconomyAdmission.Producers(o,(catalog??AiRosterCatalog.Initial).For(productionKind)).Where(b=>b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState!=null&&b.PrivateState.Lifecycle?.Selling!=true);
        internal static bool Blocked(PlayableBuildingSnapshot b)=>b.PrivateState.Orders.Any(q=>q.Active&&q.Remaining<=0);
        internal static bool Productive(PlayableBuildingSnapshot b)=>b.PrivateState.Orders.Any(q=>q.Active&&q.Remaining>0);
        public long IdleTicks(int id,long tick)=>idleSince.TryGetValue(id,out var start)?tick-start:0;
        public AiProductionCapacity Assess(PlayableAiObservation o,PlayableProfile p,AiProfile ai,long available,AiRosterCatalog catalog=null,PlayableEntityKind productionKind=PlayableEntityKind.Tank)
        {
            catalog=catalog??AiRosterCatalog.Initial;
            var descriptor=catalog.For(productionKind);
            var lines=Lines(o,catalog,productionKind).ToArray();int free=o.Population==null?0:Math.Max(0,o.Population.Capacity-o.Population.Living-o.Population.Reserved);
            // Caller selects the unit; composition is unchanged. Descriptor terms come from gameplay.
            int cost=(int)catalog.CreditCost(productionKind,p),pop=catalog.PopulationCost(productionKind,p);
            double capacity=0;int blocked=0,waiting=0;
            foreach(var b in lines)
            {
                var head=b.PrivateState.Orders.FirstOrDefault();
                if(Blocked(b)){blocked++;continue;}
                if(head!=null&&!head.Active){waiting++;continue;}
                if(head!=null)capacity+=head.PaidCost/head.Duration;
                else if(free>=pop&&string.IsNullOrWhiteSpace(descriptor.exclusionReason)&&descriptor.unlockDependencies.All(r=>o.OwnerResearch.Any(q=>q.Kind==r&&q.Complete)))capacity+=cost/catalog.ProductionSeconds(productionKind,p);
            }
            long span=samples.Count==0?0:o.Tick-samples[0].Tick;
            double income=span<=0?0:(o.SettledIncome-samples[0].Income)*30d/span;
            double busyTime=0,lineTime=0;
            for(int i=1;i<samples.Count;i++){long dt=samples[i].Tick-samples[i-1].Tick;busyTime+=samples[i-1].Busy*dt;lineTime+=samples[i-1].Lines*dt;}
            double utilization=lineTime==0?0:busyTime/lineTime;
            long fund=(long)TerritoryRules.Cost(p,PlayableBuildingKind.Factory)+(long)ai.Value("economy.factoryLaunchCycles")*PlayableUnitRules.Cost(p,PlayableEntityKind.Tank);
            bool constructing=o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Health>0&&b.Phase!=ConstructionPhase.Ready);
            string reason=lines.Length==0?"no existing live line":blocked>0?"producer exit blocked":waiting>0||free<pop?"population capacity":constructing?"factory construction pending":lines.Any(b=>b.PrivateState.QueueCount==0)?"fill existing idle line":span<window?"income window warming":utilization<ai.Value("economy.factoryUtilization")?"line utilization below scaling threshold":income<=capacity?"income within sustained spend capacity":o.Credits<=samples[0].Bank?"bank not growing":available<fund?"factory startup fund unavailable":null;
            return new AiProductionCapacity(income,capacity,utilization,span,blocked,waiting,fund,reason);
        }
        internal void Validate(long tick,double settled,IReadOnlyList<int> idleProducers)
        {
            AiStateWire.Require(samples.All(s=>s.Tick<=tick&&s.Income<=settled),"income history/world binding");
            AiStateWire.Require(samples.Count==0||samples[samples.Count-1].Tick!=tick||samples[samples.Count-1].Income==settled,"latest settled income binding");
            AiStateWire.Require(idleSince.All(x=>x.Value<=tick),"idle history clock binding");
            // Eliminated owners retain historical observations; live same-tick IDs must match.
            if(samples.Count>0&&samples[samples.Count-1].Tick==tick)AiStateWire.Require(idleSince.Keys.All(idleProducers.Contains),"idle history/producer binding");
        }
        internal void WriteState(BinaryWriter w)
        {
            w.Write(window);WorldWire.Array(w,samples.ToArray(),s=>{w.Write(s.Tick);Number(w,s.Income);w.Write(s.Bank);w.Write(s.Lines);w.Write(s.Busy);});
            WorldWire.Array(w,idleSince.ToArray(),s=>{w.Write(s.Key);w.Write(s.Value);});
        }
        internal static AiProductionDemand ReadState(BinaryReader r,long tick,AiProfile ai)
        {
            var d=new AiProductionDemand{window=r.ReadInt64()};
            AiStateWire.Require(d.window==0||d.window==AiProfile.SecondsToTicks(ai.Value("economy.incomeWindowSeconds"),30),"income window binding");
            d.samples.AddRange(WorldWire.Array(r,()=>new Sample{Tick=r.ReadInt64(),Income=Number(r),Bank=r.ReadInt32(),Lines=r.ReadInt32(),Busy=r.ReadInt32()}));
            AiStateWire.Require(d.samples.Count<=AiProfile.SecondsToTicks(AiProfileMetadata.Field("economy.incomeWindowSeconds").Maximum,30)+2,"income history bound");
            for(int i=0;i<d.samples.Count;i++)
            {var s=d.samples[i];AiStateWire.Require(s.Tick>=0&&s.Tick<=tick&&s.Income>=0&&s.Bank>=0&&s.Lines>=0&&s.Busy>=0&&s.Busy<=s.Lines&&(i==0||s.Tick>d.samples[i-1].Tick&&s.Income>=d.samples[i-1].Income),"income history values/order");}
            foreach(var x in WorldWire.Array(r,()=>new KeyValuePair<int,long>(r.ReadInt32(),r.ReadInt64())))
            {AiStateWire.Require(x.Key>0&&x.Value>=0&&x.Value<=tick&&!d.idleSince.ContainsKey(x.Key),"idle line history");d.idleSince.Add(x.Key,x.Value);}
            return d;
        }
        internal void Rebind(AiProfile ai,long tick)
        {window=AiProfile.SecondsToTicks(ai.Value("economy.incomeWindowSeconds"),30);while(samples.Count>1&&samples[1].Tick<=tick-window)samples.RemoveAt(0);}
    }
    internal sealed partial class PlayableDomain
    {
        // Restore validation is a pure authority read. PlayerSnapshot lazily creates
        // research rows, so invoking it here would mutate a valid cold state.
        internal void ValidateAiProductionDemand(AiProductionDemand history,string id)
        {
            var owner=OwnerFor(id);
            history.Validate(Tick,SettledIncome(owner),buildings.Values.Where(b=>b.Owner==owner&&b.Kind==PlayableBuildingKind.Factory&&b.Health>0&&b.Ready&&b.Sale==null&&b.Orders.Count==0).Select(b=>b.Id).ToArray());
        }
    }
    public sealed class AiProductionCapacity
    {
        internal AiProductionCapacity(double income,double capacity,double utilization,long measured,int exits,int population,long startup,string reason)
        {SettledIncomePerSecond=income;SustainedSpendPerSecond=capacity;Utilization=utilization;MeasuredTicks=measured;BlockedExits=exits;PopulationWaitingLines=population;StartupFund=startup;ScalingReason=reason;}
        public double SettledIncomePerSecond{get;} public double SustainedSpendPerSecond{get;} public double Utilization{get;}
        public long MeasuredTicks{get;} public int BlockedExits{get;} public int PopulationWaitingLines{get;} public long StartupFund{get;}
        public string ScalingReason{get;} public bool CanScale=>ScalingReason==null;
    }
}

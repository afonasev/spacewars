using System;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed class AiRelayBid
    {
        internal int Site;
        internal long Tick, Started;
        internal double Arrival, Capture, Force, Resistance, Clearance;
        internal bool Breaks;
    }

    // One owner commitment. The existing arbiter wire carries this memo through
    // background review clones and saves, with no second authority clock.
    internal sealed class AiRelayAssault
    {
        internal const string MemoKey="relay-assault-v1";
        internal int Site;
        internal NavPoint Goal;
        internal long ProgressTick;
        internal double Distance=double.MaxValue, Progress=-1, EnemyHealth=double.MaxValue;
        internal int Owner=-1;
        internal AiRelayBid[] Bids=Array.Empty<AiRelayBid>();

        internal bool Observe(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDecisionArbiter arbiter)
        {
            if(Site==0)return false;
            var relay=o.PublicRelaySites.FirstOrDefault(s=>s.Id==Site);
            var members=o.Entities.Where(u=>AiRelayObjectives.Available(o,u)&&
                (AiRelayObjectives.QueuedApproach(u.Queue?.Active,Site,Goal)||u.Queue?.Active==null&&u.CurrentOrder is PlayableTacticalOrderSnapshot order&&
                 (order.Kind is PlayableTacticalOrderKind.Move or PlayableTacticalOrderKind.AttackMove)&&order.Destination.Equals(Goal))).ToArray();
            if(relay==null||members.Length==0)return false;
            var visible=o.Sites.FirstOrDefault(s=>s.Site.Id==Site);
            double distance=members.Average(u=>AiOpponentSelector.Distance(u.Position,relay.Position));
            double health=AiRelayObjectives.Enemies(o,p,relay.Position).Sum(u=>(double)u.Health);
            int owner=AiRelayObjectives.Team(o,Site);
            bool progress=owner!=Owner||visible!=null&&Math.Abs(visible.Progress-Progress)>1e-9||
                distance+p.Navigation.ArrivalTolerance<Distance||health<EnemyHealth;
            if(progress){ProgressTick=o.Tick;Distance=distance;EnemyHealth=health;Owner=owner;Progress=visible?.Progress??-1;}
            long stall=AiProfile.SecondsToTicks(ai.Value("armies.stallSeconds"),30);
            bool outside=members.Any(u=>!u.Moving&&u.NavigationOutcome==NavigationOutcome.Arrived&&
                AiOpponentSelector.Distance(u.Position,relay.Position)>p.RelayCaptureRadius);
            if(!outside&&o.Tick-ProgressTick<stall)return false;
            arbiter.RejectDominationApproach(Site,Goal,o.Tick,stall);
            return true;
        }

        internal void Begin(int site,NavPoint goal,long tick)
        {
            if(Site==site&&Goal.Equals(goal))return;
            Site=site;Goal=goal;ProgressTick=tick;Distance=double.MaxValue;Progress=-1;EnemyHealth=double.MaxValue;Owner=-1;
        }

        internal string Pack()=>Convert.ToBase64String(WorldWire.Pack(w=>{
            w.Write(Site);w.Write(Goal.X);w.Write(Goal.Z);w.Write(ProgressTick);
            w.Write(Distance);w.Write(Progress);w.Write(EnemyHealth);w.Write(Owner);
            w.Write(Bids.Length);
            foreach(var bid in Bids){w.Write(bid.Site);w.Write(bid.Tick);w.Write(bid.Started);w.Write(bid.Arrival);w.Write(bid.Capture);
                w.Write(bid.Force);w.Write(bid.Resistance);w.Write(bid.Clearance);w.Write(bid.Breaks);}
        }));

        internal static AiRelayAssault Unpack(string value,long tick)
        {
            if(value==null)return new AiRelayAssault();
            using var r=new BinaryReader(new MemoryStream(Convert.FromBase64String(value)));
            var state=new AiRelayAssault{Site=r.ReadInt32(),Goal=new NavPoint(WorldWire.Number(r),WorldWire.Number(r)),
                ProgressTick=r.ReadInt64(),Distance=WorldWire.Number(r),Progress=WorldWire.Number(r),
                EnemyHealth=WorldWire.Number(r),Owner=r.ReadInt32()};
            int count=r.ReadInt32();
            if(state.Site<0||state.ProgressTick<0||state.ProgressTick>tick||count<0||count>1024)
                throw new ArgumentException("Invalid relay assault memo.");
            state.Bids=Enumerable.Range(0,count).Select(_=>new AiRelayBid{Site=r.ReadInt32(),Tick=r.ReadInt64(),Started=r.ReadInt64(),
                Arrival=WorldWire.Number(r),Capture=WorldWire.Number(r),Force=WorldWire.Number(r),
                Resistance=WorldWire.Number(r),Clearance=WorldWire.Number(r),Breaks=WorldWire.Boolean(r)}).ToArray();
            if(state.Bids.Any(b=>b.Site<=0||b.Tick<0||b.Tick>tick||b.Arrival<0||b.Capture<0||b.Force<0||b.Resistance<0||b.Clearance<0)||r.BaseStream.Position!=r.BaseStream.Length)
                throw new ArgumentException("Invalid relay assault bids.");
            return state;
        }
    }

    public sealed partial class AiDecisionArbiter
    {
        internal AiRelayAssault RelayAssault(long tick)=>AiRelayAssault.Unpack(
            history.TryGetValue(AiRelayAssault.MemoKey,out var memo)?memo.Conditions:null,tick);
        internal void SaveRelayAssault(AiRelayAssault state)=>history[AiRelayAssault.MemoKey]=new History{Conditions=state.Pack()};
    }
}

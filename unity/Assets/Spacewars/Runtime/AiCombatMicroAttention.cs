using System;
using System.IO;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Fixed-point token bucket: one useful order costs 30 ticks/s * 60 s.
    // These are clock/unit conversions, not balance parameters.
    internal sealed class AiCombatMicroAttention
    {
        const long Order=1800;
        long tick=-1,credit;
        int streak;
        bool lastMicro;
        internal long Applied {get;private set;}
        internal long LastTick=>tick;
        internal long Credit=>credit;
        internal void Advance(long now,AiProfile p,AiDifficulty d)
        {
            long capacity=checked((long)p.DifficultyValue(d,"control.burst")*Order);
            if(tick<0){credit=capacity;tick=now;return;}
            if(now<tick)throw new InvalidOperationException("Attention clock reversed.");
            long rate=(long)p.DifficultyValue(d,"control.ordersPerMinute");
            // Saturate before multiplication; no unlimited bank after idle or pause.
            long elapsed=Math.Min(now-tick,capacity/rate+1);
            credit=Math.Min(capacity,credit+elapsed*rate);tick=now;
        }
        internal bool Allows(long now,AiProfile p,AiDifficulty d,bool micro,bool otherWaiting)
        {
            Advance(now,p,d);
            return credit>=Order&&(!otherWaiting||micro!=lastMicro||streak<Math.Max(1,(int)p.DifficultyValue(d,"control.fairRun")));
        }
        internal void Charge(long now,AiProfile p,AiDifficulty d,bool micro)
        {
            Advance(now,p,d);if(credit<Order)throw new InvalidOperationException("Useful order exceeded attention budget.");
            credit-=Order;streak=lastMicro==micro?Math.Min(int.MaxValue-1,streak)+1:1;lastMicro=micro;Applied++;
        }
        internal AiCombatMicroAttention Copy()=>new AiCombatMicroAttention{tick=tick,credit=credit,streak=streak,lastMicro=lastMicro,Applied=Applied};
        internal void Write(BinaryWriter w){w.Write(tick);w.Write(credit);w.Write(streak);w.Write(lastMicro);w.Write(Applied);}
        internal static AiCombatMicroAttention Read(BinaryReader r,long now,AiProfile p,AiDifficulty d)
        {
            var a=new AiCombatMicroAttention{tick=r.ReadInt64(),credit=r.ReadInt64(),streak=r.ReadInt32(),lastMicro=WorldWire.Boolean(r),Applied=r.ReadInt64()};
            AiStateWire.Require(a.tick>=-1&&a.tick<=now&&a.credit>=0&&a.credit<=(long)p.DifficultyValue(d,"control.burst")*Order&&a.streak>=0&&a.Applied>=0&&(a.tick!=-1||a.credit==0&&a.Applied==0&&a.streak==0),"combat attention state");return a;
        }
    }
}

using System.IO;
using System.Linq;
using static Spacewars.Runtime.WorldWire;

namespace Spacewars.Runtime
{
    internal static class AiBudgetStateWire
    {
        internal static void Write(BinaryWriter w,AiBudgetLedger ledger)
        {
            var s=ledger.Capture();String(w,s.OwnerId);w.Write(s.Generation);w.Write(s.Liquid);w.Write(s.SafetyReserve);w.Write(s.PaidTotal);w.Write(s.ReservationLifetimeTicks);
            Number(w,s.RepairPaidTotal);w.Write(s.AdmittedDecision);w.Write(s.AdmittedAction);
            WorldWire.Array(w,s.Reserved.ToArray(),x=>WriteEntry(w,x));
            WorldWire.Array(w,s.Unpaid.ToArray(),x=>WriteEntry(w,x));
            WorldWire.Array(w,s.Paid.ToArray(),x=>WriteEntry(w,x));
        }
        internal static AiBudgetLedger Read(BinaryReader r,long tick)
        {
            var owner=String(r);var generation=r.ReadInt64();var liquid=r.ReadInt32();var safety=r.ReadInt32();var paid=r.ReadInt64();var lifetime=r.ReadInt64();
            var repairPaid=Number(r);var admittedDecision=r.ReadInt64();var admittedAction=r.ReadInt32();
            AiStateWire.Require(lifetime>0,"budget horizon");
            return AiBudgetLedger.Restore(new AiBudgetState(owner,generation,liquid,safety,paid,
                WorldWire.Array(r,()=>ReadEntry(r)),WorldWire.Array(r,()=>ReadEntry(r)),WorldWire.Array(r,()=>ReadEntry(r)),lifetime,admittedDecision,admittedAction,repairPaid),tick);
        }
        private static void WriteEntry(BinaryWriter w,AiBudgetEntry x)
        {
            String(w,x.Id);String(w,x.Purpose);AiStateWire.Write(w,x.Action);w.Write(x.Amount);w.Write(x.Priority);
            w.Write(x.CreatedTick);w.Write(x.ExpiresTick);String(w,x.ReleaseCondition);WorldWire.Array(w,x.Claims.ToArray(),c=>String(w,c));
            String(w,x.Terms);AiStateWire.Write(w,x.Receipt);w.Write(x.AwaitingPayment);Number(w,x.ExactSettled);
        }
        private static AiBudgetEntry ReadEntry(BinaryReader r)=>new AiBudgetEntry(String(r),String(r),AiStateWire.ReadPlayableAiAction(r),
            r.ReadInt32(),r.ReadInt32(),r.ReadInt64(),r.ReadInt64(),String(r),WorldWire.Array(r,()=>String(r)),String(r),AiStateWire.ReadAiReceiptIdentity(r),Boolean(r),Number(r));
    }
}

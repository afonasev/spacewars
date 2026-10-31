using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class Q10ReceiptAttributionTests
    {
        static PlayableCommand Command(string owner,long sequence)=>new PlayableCommand(7,sequence,owner,PlayableCommandKind.Move,new[]{1,2},new NavPoint(1,2));
        [Test] public void EightExpectedOwnerSequencePairsKeepAllActualStatusesAndIgnoreForeignCollisions()
        {
            var lane=new QaPerformanceReceipts();var commands=Enumerable.Range(0,8).Select(i=>Command("owner-"+i,100)).ToArray();
            foreach(var c in commands){lane.Register(c);lane.Submitted(c,PlayableCommandStatus.Accepted,10);}
            for(int i=7;i>=0;i--){var c=commands[i];var status=i%3==0?PlayableCommandStatus.Cancelled:i%3==1?PlayableCommandStatus.Rejected:PlayableCommandStatus.Applied;lane.Receipt(new PlayableCommandReceipt(c.Sequence,12,status,null,i,c.PlayerId),"guard-owner-"+i);}
            lane.Receipt(new PlayableCommandReceipt(100,12,PlayableCommandStatus.Applied,null,0,"foreign"));lane.Receipt(new PlayableCommandReceipt(101,12,PlayableCommandStatus.Applied,null,0,"owner-0"));
            var rows=lane.Copy();Assert.AreEqual(8,lane.Expected);Assert.Zero(lane.Dropped);Assert.AreEqual(16,rows.Length);
            foreach(var c in commands){var pair=rows.Where(r=>r.Owner==c.PlayerId&&r.Sequence==100).ToArray();Assert.AreEqual(2,pair.Length);Assert.True(pair.All(r=>r.Generation==7));Assert.AreEqual("submission",pair[0].Phase);Assert.AreEqual(PlayableCommandStatus.Accepted,pair[0].Status);Assert.AreEqual("receipt",pair[1].Phase);Assert.IsNull(pair[0].AdmissionDiagnostic);Assert.AreEqual("guard-"+c.PlayerId,pair[1].AdmissionDiagnostic);}
            Assert.AreEqual(3,rows.Count(r=>r.Phase=="receipt"&&r.Status==PlayableCommandStatus.Cancelled));Assert.AreEqual(3,rows.Count(r=>r.Phase=="receipt"&&r.Status==PlayableCommandStatus.Rejected));Assert.AreEqual(2,rows.Count(r=>r.Phase=="receipt"&&r.Status==PlayableCommandStatus.Applied));
        }
        [Test] public void MissingReceiptStaysUnknownAndBoundedOverflowIsExplicit()
        {
            var lane=new QaPerformanceReceipts();var c=Command("owner-0",1);lane.Register(c);lane.Submitted(c,PlayableCommandStatus.Accepted,10);
            Assert.AreEqual(1,lane.Copy().Length);Assert.False(lane.Copy().Any(r=>r.Phase=="receipt"),"Missing CurrentOrder/receipt must never be synthesized as rejection.");
            for(int i=0;i<70;i++)lane.Receipt(new PlayableCommandReceipt(1,i,PlayableCommandStatus.Applied,null,0,c.PlayerId));
            Assert.AreEqual(64,lane.Copy().Length);Assert.AreEqual(7,lane.Dropped);
            lane.Register(Command(c.PlayerId,2));Assert.AreEqual(8,lane.Dropped);Assert.AreEqual(1,lane.Expected);
        }
        [Test] public void ActualRejectedMessageIsBoundedAndCsvPreservesExistingColumnsAndQuotes()
        {
            var lane=new QaPerformanceReceipts();var c=Command("owner-0",1);lane.Register(c);lane.Submitted(c,PlayableCommandStatus.Accepted,10);
            string message="Actual, \"rejection\"\n"+new string('x',600),diagnostic="tactical-final:ReadsetChanged:\"actual source\"";
            var receipt=new PlayableCommandReceipt(1,12,PlayableCommandStatus.Rejected,message,4.5,c.PlayerId);lane.Receipt(receipt,diagnostic);
            var rows=lane.Copy();Assert.AreEqual(2,rows.Length);Assert.IsNull(rows[0].ReceiptMessage);Assert.AreEqual(PlayableCommandStatus.Rejected,rows[1].Status);
            Assert.AreEqual(message.Substring(0,QaPerformanceReceipts.ReceiptMessageCapacity),rows[1].ReceiptMessage);Assert.AreEqual(message,receipt.Message,"Only the diagnostic copy may be bounded.");
            Assert.AreEqual("owner,generation,sequence,tick,phase,status,monotonic_seconds,latency_ms,admission_diagnostic",string.Join(",",QaPerformanceReceipts.CsvHeader.Split(',').Take(9)));
            Assert.AreEqual("receipt_message",QaPerformanceReceipts.CsvHeader.Split(',')[9]);
            var csv=QaPerformanceReceipts.CsvRow(rows[1]);StringAssert.StartsWith("owner-0,7,1,12,receipt,Rejected,",csv);
            StringAssert.EndsWith(",\""+diagnostic.Replace("\"","\"\"")+"\",\""+rows[1].ReceiptMessage.Replace("\"","\"\"")+"\"",csv);
            Assert.Zero(lane.Dropped);
        }
    }
}

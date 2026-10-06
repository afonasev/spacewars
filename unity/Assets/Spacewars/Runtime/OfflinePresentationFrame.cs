using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed class OfflineReceipt
    {
        public long Ordinal { get; }
        public PlayableCommandReceipt Receipt { get; }
        internal OfflineReceipt(long ordinal, PlayableCommandReceipt receipt) { Ordinal=ordinal; Receipt=receipt; }
    }

    // Published once per authority cycle. A viewport must never independently read
    // ParticipantView or drain receipts while composing a multi-seat render frame.
    public sealed class OfflinePresentationFrame
    {
        public long Generation { get; }
        public long Sequence { get; }
        public long Tick { get; }
        public IReadOnlyDictionary<string,PlayableSnapshot> Views { get; }
        public IReadOnlyList<OfflineReceipt> Receipts { get; }
        public long ReceiptOrdinal { get; }
        internal OfflinePresentationFrame(Dictionary<string,PlayableSnapshot> views, OfflineReceipt[] receipts, long ordinal)
        {
            if(views.Count==0)throw new ArgumentException("Missing owner projections.");
            var first=views.Values.First();Generation=first.Generation;Sequence=first.Sequence;Tick=first.Tick;
            if(views.Any(v=>v.Key!=v.Value.OwnerId||v.Value.Generation!=Generation||v.Value.Sequence!=Sequence||v.Value.Tick!=Tick||v.Value.Paused!=first.Paused||v.Value.Status!=first.Status))
                throw new ArgumentException("Incoherent authority frame.");
            if(receipts.Any(r=>r.Receipt.AppliedTick>Tick||!views.ContainsKey(r.Receipt.OwnerId??"")))
                throw new ArgumentException("Receipt outside frame authority.");
            Views=new ReadOnlyDictionary<string,PlayableSnapshot>(new Dictionary<string,PlayableSnapshot>(views));
            Receipts=Array.AsReadOnly((OfflineReceipt[])receipts.Clone());ReceiptOrdinal=ordinal;
        }
    }
}

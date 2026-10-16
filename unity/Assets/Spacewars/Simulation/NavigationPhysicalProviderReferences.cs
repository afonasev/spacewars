using System;

namespace Spacewars.Simulation
{
    internal sealed partial class NavProviderReferences : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen&&!faulted&&reserve==null;
        internal System.Collections.Generic.IEnumerable<Entry> PhysicalEntries {get{foreach(var bucket in buckets)foreach(var entry in bucket.Value)yield return entry;}}
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ProviderReferences);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(128)||NavStorageLayout.ObjectBytesBound(GetType())>NavStorageLayout.Charge(128))throw new ArgumentException("Physical provider references owner/layout contradiction.");
            writer.Output.Write(Count);writer.Output.Write(DiagnosticBytes);NavPhysicalMacro.WriteEdge(writer,this,buckets);
        }
        private NavProviderReferences(bool physicalBirth):base(128){frozen=true;}
        internal static NavProviderReferences ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int count=reader.Count(NavPhysicalCaptureIndex.MaxDefinitions);long bytes=reader.Input.ReadInt64();var buckets=NavPhysicalMacro.ReadEdge<NavCowDictionary<uint,NavPersistentQueue<Entry>>>(reader);
            if(bytes<0||buckets.HasPhysicalInsertionOrder||NavStorageLayout.ObjectBytesBound(typeof(NavProviderReferences))>NavStorageLayout.Charge(128))throw new ArgumentException("Physical provider references dimensions/layout contradiction.");
            using(NavStorageScratch.Reserve(reader.Reservation,8192)){
                int actual=0;long diagnostic=0;foreach(var pair in buckets){if(pair.Value.Count==0)throw new ArgumentException("Empty physical provider bucket.");foreach(var entry in pair.Value){if(Hash(entry.Provider)!=pair.Key)throw new ArgumentException("Physical provider hash bucket contradiction.");int aliases=0;foreach(var other in pair.Value)if(ReferenceEquals(entry.Provider,other.Provider))aliases++;if(aliases!=1)throw new ArgumentException("Duplicate physical provider reference.");actual++;diagnostic=checked(diagnostic+entry.DiagnosticBytes);}}
                if(actual!=count||diagnostic!=bytes)throw new ArgumentException("Physical provider diagnostic/count contradiction.");
            }
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(128);var value=allocation.Register(new NavProviderReferences(true){Count=count,DiagnosticBytes=bytes});value.buckets=NavPhysicalMacro.Acquire(value,buckets);return value;}
        }
        internal sealed partial class Entry : INavPhysicalBody
        {
            internal override bool IsImmutableCaptureRoot=>IsRegistered&&providerRoot!=null;
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ProviderReference);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer)
            {
                if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(128)||NavStorageLayout.ObjectBytesBound(GetType())>128||!ReferenceEquals(providerRoot,(Provider as INavStorageFacade)?.StorageRoot))throw new ArgumentException("Physical provider entry owner/layout contradiction.");
                writer.Output.Write(Count);writer.Output.Write(DiagnosticBytes);NavPhysicalMacro.WriteEdge(writer,this,providerRoot);
            }
            internal static Entry ReadPhysicalBody(NavPhysicalBodyReader reader)
            {
                int count=reader.Input.ReadInt32();long bytes=reader.Input.ReadInt64();var root=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);
                if(!(root.Facade is ILayeredNavigationProvider provider)||count<1||bytes!=provider.EstimatedRetainedBytes||NavStorageLayout.ObjectBytesBound(typeof(Entry))>128)throw new ArgumentException("Physical provider entry count/diagnostic/layout contradiction.");
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(128);var value=allocation.Register(new Entry(provider,count,bytes));value.providerRoot=NavPhysicalMacro.Acquire(value,root);return value;}
            }
        }
    }
}

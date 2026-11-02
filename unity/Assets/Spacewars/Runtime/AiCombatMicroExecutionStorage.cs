using System;
namespace Spacewars.Runtime
{
    // Private AI work admission, NOT finance/APM or a second Nav allocator.
    // The existing wire ceiling is a hard upper bound, not a claim that the sum
    // of world sections fits it. Nav continues to own upstream pin lifetimes.
    internal sealed class AiCombatMicroExecutionStorage
    {
        internal const long ControlBytes=128,RootBytes=64,HandleBytes=64;
        readonly object gate=new object();
        readonly long capacity;
        long retained=ControlBytes;
        internal AiCombatMicroExecutionStorage(long capacity=WorldWire.MaxBytes)
        {if(capacity<ControlBytes||capacity>WorldWire.MaxBytes)throw new ArgumentOutOfRangeException(nameof(capacity));this.capacity=capacity;}
        internal long Retained {get{lock(gate)return retained;}}
        internal long Capacity=>capacity;
        sealed class Root
        {internal readonly long Bytes;internal int Owners=1;internal Root(long bytes){Bytes=bytes;}}
        internal sealed class Lease:IDisposable
        {
            readonly AiCombatMicroExecutionStorage storage;
            readonly Root root;
            bool released;
            internal long PayloadBytes=>root.Bytes;
            Lease(AiCombatMicroExecutionStorage storage,Root root){this.storage=storage;this.root=root;}
            internal bool TryShare(out Lease lease)
            {lock(storage.gate){if(released)throw new ObjectDisposedException(nameof(Lease));return TryShareRetainedRoot(out lease);}}
            // Immutable payload wrappers may outlive their original owner handle.
            // A live pin can still own that payload; never resurrect a dead root.
            internal bool TryShareRetainedRoot(out Lease lease)
            {
                lease=null;lock(storage.gate){if(root.Owners==0||HandleBytes>storage.capacity-storage.retained)return false;
                    storage.retained+=HandleBytes;root.Owners++;
                    try{lease=new Lease(storage,root);return true;}catch{root.Owners--;storage.retained-=HandleBytes;throw;}
                }
            }
            public void Dispose(){lock(storage.gate){if(released)return;released=true;storage.retained-=HandleBytes;if(--root.Owners==0)storage.retained-=root.Bytes+RootBytes;}}
            internal static Lease Birth(AiCombatMicroExecutionStorage storage,long bytes)=>new Lease(storage,new Root(bytes));
        }
        internal bool TryReserve(long bytes,out Lease lease)
        {
            if(bytes<0||bytes>WorldWire.MaxBytes)throw new ArgumentOutOfRangeException(nameof(bytes));lease=null;long cost=checked(bytes+RootBytes+HandleBytes);
            lock(gate){if(cost>capacity-retained)return false;retained+=cost;try{lease=Lease.Birth(this,bytes);return true;}catch{retained-=cost;throw;}}
        }
        // Fixed-capacity transaction storage is itself admitted before birth.
        // No List growth, transfer-copy or hidden allocator is used in Advance.
        internal bool TryBegin(int roots,out Transaction transaction)
        {
            transaction=null;if(roots<0||roots>WorldWire.MaxItems)throw new ArgumentOutOfRangeException(nameof(roots));
            if(!TryReserve(checked(256L+8L*roots),out var metadata))return false;
            try{transaction=new Transaction(this,metadata,roots);return true;}catch{metadata.Dispose();throw;}
        }
        internal sealed class OwnedLeases:IDisposable
        {
            readonly Lease metadata;readonly Lease[] leases;readonly int count;bool disposed;
            internal OwnedLeases(Lease metadata,Lease[] leases,int count){this.metadata=metadata;this.leases=leases;this.count=count;}
            public void Dispose(){if(disposed)return;disposed=true;for(int i=0;i<count;i++)leases[i].Dispose();metadata.Dispose();}
        }
        internal sealed class Transaction:IDisposable
        {
            readonly AiCombatMicroExecutionStorage storage;readonly Lease metadata;readonly Lease[] leases;
            int count;bool transferred,disposed;
            internal Transaction(AiCombatMicroExecutionStorage storage,Lease metadata,int roots){this.storage=storage;this.metadata=metadata;leases=new Lease[roots];}
            internal bool TryReserve(long bytes)
            {if(transferred||disposed)throw new InvalidOperationException("Admission closed.");if(count==leases.Length)return false;if(!storage.TryReserve(bytes,out var lease))return false;leases[count++]=lease;return true;}
            internal OwnedLeases Transfer(){if(transferred||disposed)throw new InvalidOperationException("Admission closed.");var result=new OwnedLeases(metadata,leases,count);transferred=true;return result;}
            public void Dispose(){if(disposed)return;disposed=true;if(!transferred){for(int i=0;i<count;i++)leases[i].Dispose();metadata.Dispose();}}
        }
    }
}

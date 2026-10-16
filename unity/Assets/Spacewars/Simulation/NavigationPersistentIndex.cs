using System;
using System.Collections;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    // Dictionary iteration follows entry slots, including LIFO reuse after
    // removals. The free list is part of the root, rather than an external map
    // or an append-only key journal whose old keys reappear after reinsertion.
    internal sealed partial class NavPersistentIndex<TKey,TValue> : NavOwnedStorage, IReadOnlyDictionary<TKey,TValue>
    {
        internal sealed partial class Slot : NavOwnedStorage
        {
            internal readonly bool Live;
            internal readonly int FreeNext;
            internal readonly TKey Key;
            internal readonly TValue Value;
            private readonly NavOwnedStorage metadata;
            private bool acquired;
            internal Slot(TKey key,TValue value,NavOwnedStorage metadata):base(SlotBytes)
            {Live=true;FreeNext=-1;Key=key;Value=value;this.metadata=metadata;}
            internal Slot(int next):base(SlotBytes){FreeNext=next;}
            internal void Acquire(){if(metadata!=null&&IsRegistered)RequireEdge(metadata);NavStoragePayload<TKey>.Validate(Key,this);NavStoragePayload<TValue>.Validate(Value,this);metadata?.Retain();NavStoragePayload<TKey>.Retain(Key);NavStoragePayload<TValue>.Retain(Value);acquired=true;}
            protected override void ReleaseEdges(){if(!acquired)return;metadata?.Release();NavStoragePayload<TKey>.Release(Key);NavStoragePayload<TValue>.Release(Value);}
        }
        private static readonly long SlotBytes=128+NavStorageLayout.ValueBytes(typeof(TKey))+NavStorageLayout.ValueBytes(typeof(TValue));
        private NavCowDictionary<TKey,int> lookup;
        private NavCowDictionary<int,Slot> slots;
        private Func<long,bool> reserve;
        private bool frozen,faulted,released;
        private int nextSlot,freeHead=-1;
        internal static NavPersistentIndex<TKey,TValue> Create(IComparer<TKey> comparer=null,Func<long,bool> reserve=null)
        {using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(128);return new NavPersistentIndex<TKey,TValue>(comparer,reserve,true);}}
        internal NavPersistentIndex(IComparer<TKey> comparer=null,Func<long,bool> reserve=null,bool alreadyReserved=false):base(128)
        {
            this.reserve=reserve;
            using(var a=new NavStorageAllocation(reserve)){a.Reserve(128);a.Register(this);Retain();}
            try{lookup=NavCowDictionary<TKey,int>.Create(comparer,false,reserve);slots=NavCowDictionary<int,Slot>.Create(insertionOrder:false,reserve:reserve);}catch{ReleaseStorage();throw;}
        }
        private static NavPersistentIndex<TKey,TValue> Create(NavPersistentIndex<TKey,TValue> source,Func<long,bool> admission)
        {using(var allocation=new NavStorageAllocation(admission)){allocation.Reserve(128);return new NavPersistentIndex<TKey,TValue>(source,admission,true);}}
        private NavPersistentIndex(NavPersistentIndex<TKey,TValue> source,Func<long,bool> admission,bool alreadyReserved=false):base(128)
        {
            reserve=admission;
            using(var a=new NavStorageAllocation(admission)){if(!alreadyReserved)a.Reserve(128);a.Register(this);Retain();}
            try{lookup=source.lookup.ForkPrepared(admission);slots=source.slots.ForkPrepared(admission);nextSlot=source.nextSlot;freeHead=source.freeHead;}catch{ReleaseStorage();throw;}
        }
        internal static NavPersistentIndex<TKey,TValue> CreateOwned(INavStorageReservation reservation,IComparer<TKey> comparer=null)
        {if(reservation==null)throw new ArgumentNullException(nameof(reservation));return NavPersistentIndex<TKey,TValue>.Create(comparer,reservation.TryReserve);}
        internal NavPersistentIndex<TKey,TValue> ForkOwned(INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,this);return ForkPrepared(reservation.TryReserve);}
        internal NavPersistentIndex<TKey,TValue> ForkPrepared(Func<long,bool> admission)
        {
            if(!frozen||faulted||admission==null)throw new InvalidOperationException("Seal index before preparing a fork.");
            if(IsRegistered){if(!(admission.Target is INavStorageReservation owner))throw new ArgumentException("Missing index ledger.");NavStorageAdmission.RequireOwned(owner,this);}
            return NavPersistentIndex<TKey,TValue>.Create(this,admission);
        }
        internal void Seal(){if(faulted)throw new InvalidOperationException("Failed index candidate must be discarded.");lookup.Seal();slots.Seal();reserve=null;frozen=true;}
        private void Mutable(){if(frozen||faulted||released)throw new InvalidOperationException("Committed index cannot be mutated.");}
        public int Count=>lookup.Count;
        public bool ContainsKey(TKey key)=>lookup.ContainsKey(key);
        public bool TryGetValue(TKey key,out TValue value){if(lookup.TryGetValue(key,out int id)){value=slots[id].Value;return true;}value=default;return false;}
        public TValue this[TKey key]{get{if(!TryGetValue(key,out var value))throw new KeyNotFoundException();return value;}}
        internal void Add(TKey key,TValue value,NavOwnedStorage metadata=null)
        {
            Mutable();if(lookup.ContainsKey(key))throw new ArgumentException("Duplicate persistent index key.");
            // String keys borrow immutable recipe strings. Their owning recipe
            // remains an explicit edge for the entire live index entry.
            if(IsRegistered){if(typeof(TKey)==typeof(string)&&metadata==null)throw new ArgumentException("String index key requires its owned metadata.");if(metadata!=null)RequireEdge(metadata);}
            NavStoragePayload<TKey>.Validate(key,this);NavStoragePayload<TValue>.Validate(value,this);
            if(freeHead==-1&&nextSlot==int.MaxValue)throw new InvalidOperationException("Index slots exhausted.");
            try{int id=freeHead==-1?nextSlot:freeHead;int following=freeHead==-1?-1:slots[id].FreeNext;
                using(var a=new NavStorageAllocation(reserve)){a.Reserve(SlotBytes);var entry=a.Register(new Slot(key,value,metadata));entry.Acquire();slots[id]=entry;lookup.Add(key,id);}
                if(freeHead==-1)nextSlot++;else freeHead=following;
            }catch{faulted=true;throw;}
        }
        internal bool Remove(TKey key)
        {
            Mutable();if(!lookup.TryGetValue(key,out int id))return false;
            try{using(var a=new NavStorageAllocation(reserve)){a.Reserve(SlotBytes);var empty=a.Register(new Slot(freeHead));slots[id]=empty;lookup.Remove(key);}freeHead=id;return true;}catch{faulted=true;throw;}
        }
        internal void Clear()
        {
            Mutable();NavCowDictionary<TKey,int> freshLookup=null;NavCowDictionary<int,Slot> freshSlots=null;
            try{freshLookup=NavCowDictionary<TKey,int>.Create(lookup.KeyComparer,false,reserve);freshSlots=NavCowDictionary<int,Slot>.Create(insertionOrder:false,reserve:reserve);
                var oldLookup=lookup;var oldSlots=slots;lookup=freshLookup;slots=freshSlots;freshLookup=null;freshSlots=null;nextSlot=0;freeHead=-1;oldLookup.Release();oldSlots.Release();
            }catch{faulted=true;throw;}finally{freshLookup?.ReleaseStorage();freshSlots?.ReleaseStorage();}
        }
        public IEnumerable<TKey> Keys{get{foreach(var item in this)yield return item.Key;}}
        public IEnumerable<TValue> Values{get{foreach(var item in this)yield return item.Value;}}
        public IEnumerator<KeyValuePair<TKey,TValue>> GetEnumerator(){foreach(var item in slots){var slot=item.Value;if(slot.Live)yield return new KeyValuePair<TKey,TValue>(slot.Key,slot.Value);}}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){lookup?.Release();slots?.Release();}
    }
    internal readonly struct NavSubscriberLink
    {
        internal readonly long Item1,Item2;
        internal NavSubscriberLink(long job,long subscriber){Item1=job;Item2=subscriber;}
    }
}

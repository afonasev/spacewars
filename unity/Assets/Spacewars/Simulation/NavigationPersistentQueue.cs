using System;
using System.Collections;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    // Links are stable numeric IDs in a persistent map. No physical link points
    // back to a neighbour: changing two links copies only bounded search paths,
    // and a pinned old root keeps its exact order without ownership cycles.
    internal sealed partial class NavPersistentQueue<T> : NavOwnedStorage, IEnumerable<T>
    {
        internal sealed partial class Record : NavOwnedStorage
        {
            internal static readonly int PhysicalBodyBytes=112+NavStorageLayout.ValueBytes(typeof(T));
            internal readonly long Previous,Next;
            internal readonly T Value;
            private bool acquired;
            internal Record(long previous,long next,T value):base(PhysicalBodyBytes)
            {Previous=previous;Next=next;Value=value;}
            internal void Acquire(){NavStoragePayload<T>.Validate(Value,this);NavStoragePayload<T>.Retain(Value);acquired=true;}
            protected override void ReleaseEdges(){if(acquired)NavStoragePayload<T>.Release(Value);}
        }
        private NavCowDictionary<long,Record> records;
        private Func<long,bool> reserve;
        private bool frozen,released,faulted;
        private long nextId=1;
        internal long NextId=>nextId;
        internal long FirstId {get;private set;}
        internal long LastId {get;private set;}
        internal int Count=>records.Count;
        internal T First=>FirstId==0?default:records[FirstId].Value;
        internal T this[long id]=>records[id].Value;
        internal bool TryGetValue(long id,out T value){if(records.TryGetValue(id,out var record)){value=record.Value;return true;}value=default;return false;}
        internal static NavPersistentQueue<T> Create(Func<long,bool> reserve=null)
        {using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(128);return new NavPersistentQueue<T>(reserve,true);}}
        internal NavPersistentQueue(Func<long,bool> reserve=null,bool alreadyReserved=false):base(128)
        {
            this.reserve=reserve;
            using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(128);allocation.Register(this);Retain();}
            try{records=NavCowDictionary<long,Record>.Create(insertionOrder:false,reserve:reserve);}catch{ReleaseStorage();throw;}
        }
        internal static NavPersistentQueue<T> CreateOwned(INavStorageReservation reservation)
        {if(reservation==null)throw new ArgumentNullException(nameof(reservation));return NavPersistentQueue<T>.Create(reservation.TryReserve);}
        private static NavPersistentQueue<T> Create(Func<long,bool> admission,NavPersistentQueue<T> source)
        {using(var allocation=new NavStorageAllocation(admission)){allocation.Reserve(128);return new NavPersistentQueue<T>(admission,source,true);}}
        private NavPersistentQueue(Func<long,bool> admission,NavPersistentQueue<T> source,bool alreadyReserved=false):base(128)
        {
            reserve=admission;
            using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(128);allocation.Register(this);Retain();}
            try{records=source.records.ForkPrepared(admission);FirstId=source.FirstId;LastId=source.LastId;nextId=source.nextId;}catch{ReleaseStorage();throw;}
        }
        internal NavPersistentQueue<T> ForkOwned(INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,this);if(!frozen)throw new InvalidOperationException("Seal queue before preparing a fork.");return NavPersistentQueue<T>.Create(reservation.TryReserve,this);}
        internal NavPersistentQueue<T> ForkPrepared(Func<long,bool> admission)
        {if(admission==null)throw new ArgumentNullException(nameof(admission));if(IsRegistered){if(!(admission.Target is INavStorageReservation reservation))throw new ArgumentException("Missing queue ledger.");NavStorageAdmission.RequireOwned(reservation,this);}if(!frozen||faulted)throw new InvalidOperationException("Seal queue before preparing a fork.");return NavPersistentQueue<T>.Create(admission,this);}
        internal void Seal(){if(faulted)throw new InvalidOperationException("Failed queue candidate must be discarded.");frozen=true;reserve=null;records.Seal();}
        private void Mutable(){if(frozen||released||faulted)throw new InvalidOperationException("Committed queue cannot be mutated.");}
        private void Put(long id,long previous,long next,T value)
        {
            using(var allocation=new NavStorageAllocation(reserve)){
                allocation.Reserve(Record.PhysicalBodyBytes);var record=allocation.Register(new Record(previous,next,value));record.Acquire();records[id]=record;
            }
        }
        internal long AddLast(T value)
        {
            Mutable();NavStoragePayload<T>.Validate(value,this);if(nextId==long.MaxValue)throw new InvalidOperationException("Queue ID exhausted.");
            try{long id=nextId++;Put(id,LastId,0,value);
            if(LastId!=0){var previous=records[LastId];Put(LastId,previous.Previous,id,previous.Value);}else FirstId=id;
            LastId=id;return id;}catch{faulted=true;throw;}
        }
        internal void Replace(long id,T value)
        {Mutable();NavStoragePayload<T>.Validate(value,this);var record=records[id];try{Put(id,record.Previous,record.Next,value);}catch{faulted=true;throw;}}
        internal void Remove(long id)
        {
            Mutable();var record=records[id];try{
            if(record.Previous==0)FirstId=record.Next;else{var previous=records[record.Previous];Put(record.Previous,previous.Previous,record.Next,previous.Value);}
            if(record.Next==0)LastId=record.Previous;else{var next=records[record.Next];Put(record.Next,record.Previous,next.Next,next.Value);}
            records.Remove(id);}catch{faulted=true;throw;}
        }
        internal void RemoveFirst(){if(FirstId==0)throw new InvalidOperationException("Empty queue.");Remove(FirstId);}
        internal void MoveLast(long id)
        {
            Mutable();var record=records[id];if(id==LastId)return;try{
            if(record.Previous==0)FirstId=record.Next;else{var previous=records[record.Previous];Put(record.Previous,previous.Previous,record.Next,previous.Value);}
            var next=records[record.Next];Put(record.Next,record.Previous,next.Next,next.Value);
            var tail=records[LastId];Put(LastId,tail.Previous,id,tail.Value);Put(id,LastId,0,record.Value);LastId=id;}catch{faulted=true;throw;}
        }
        internal void Clear(){Mutable();while(FirstId!=0)RemoveFirst();}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){records?.Release();}
        internal IEnumerable<System.Collections.Generic.KeyValuePair<long,T>> Entries{get{for(long id=FirstId;id!=0;){var record=records[id];yield return new System.Collections.Generic.KeyValuePair<long,T>(id,record.Value);id=record.Next;}}}
        public IEnumerator<T> GetEnumerator(){for(long id=FirstId;id!=0;){var record=records[id];yield return record.Value;id=record.Next;}}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}

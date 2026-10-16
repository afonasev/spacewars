using System;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal static class NavigationCommandLayout
    {
        internal static int Capacity<TKey,TValue>(IDictionary<TKey,TValue> map)=>map is NavigationCommandSlots<TKey,TValue> owned?owned.Capacity:Math.Max(4,Capture(map)[0]);
        internal static int Length<TKey,TValue>(IDictionary<TKey,TValue> map)=>map is AuthoritySlotMap<TKey,TValue> ordinary?ordinary.LayoutLength:((NavigationCommandSlots<TKey,TValue>)map).LayoutLength;
        internal static int[] Capture<TKey,TValue>(IDictionary<TKey,TValue> map)=>map is AuthoritySlotMap<TKey,TValue> ordinary?ordinary.CaptureLayout():((NavigationCommandSlots<TKey,TValue>)map).CaptureLayout();
        internal static void Restore<TKey,TValue>(IDictionary<TKey,TValue> map,int[] layout){if(map is AuthoritySlotMap<TKey,TValue> ordinary)ordinary.RestoreLayout(layout);else ((NavigationCommandSlots<TKey,TValue>)map).RestoreLayout(layout);}
    }
    // Navigation-only authority backing. Concrete arrays, including spare slots,
    // hash chains and free-slot order, are admitted before allocation. No BCL
    // dictionary/list capacity estimate participates in its physical charge.
    internal sealed class NavigationCommandSlots<TKey,TValue> : NavOwnedStorage, IDictionary<TKey,TValue>
    {
        private static readonly long Header=NavStorageLayout.ObjectBytesBound(typeof(NavigationCommandSlots<TKey,TValue>));
        private readonly TKey[] keys;private readonly TValue[] values;private readonly bool[] live;
        private readonly int[] next,buckets,free;private readonly NavOwnedStorage[] edges;
        private int length,count,freeCount,version;private bool released,sealedBuilder;private readonly bool referenceKeys;
        internal int Revision=>version;internal int Capacity=>keys.Length;internal int LayoutLength=>freeCount+1;internal bool UsesReferenceKeys=>referenceKeys;
        private static long Aligned(long bytes)=>checked((bytes+7L)/8L*8L);
        private static long Layout(int capacity)=>checked(Header+7*32L+Aligned((long)capacity*NavStorageLayout.ValueBytes(typeof(TKey)))+Aligned((long)capacity*NavStorageLayout.ValueBytes(typeof(TValue)))+Aligned(capacity)+3*Aligned(4L*capacity)+Aligned(8L*capacity));
        private NavigationCommandSlots(int capacity,bool referenceKeys):base(Layout(capacity))
        {this.referenceKeys=referenceKeys;keys=new TKey[capacity];values=new TValue[capacity];live=new bool[capacity];next=new int[capacity];buckets=new int[capacity];free=new int[capacity];edges=new NavOwnedStorage[capacity];for(int i=0;i<capacity;i++){buckets[i]=-1;next[i]=-1;}Retain();}
        internal static NavigationCommandSlots<TKey,TValue> CreateOwned(int capacity,INavStorageReservation reservation,bool referenceKeys=false)
            =>Allocate(capacity,reservation,referenceKeys,false);
        internal static NavigationCommandSlots<TKey,TValue> CreateEmptyOwned(INavStorageReservation reservation,bool referenceKeys=false)
            =>Allocate(0,reservation,referenceKeys,true);
        private static NavigationCommandSlots<TKey,TValue> Allocate(int capacity,INavStorageReservation reservation,bool referenceKeys,bool empty)
        {
            if(capacity<(empty?0:1)||capacity>AuthoritySlotMap<TKey,TValue>.MaximumSlots||referenceKeys&&typeof(TKey).IsValueType)throw new ArgumentException("Invalid navigation slot capacity/key mode.");
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout(capacity));return allocation.Register(new NavigationCommandSlots<TKey,TValue>(capacity,referenceKeys));}
        }
        internal void CopyLayoutFrom(IDictionary<TKey,TValue> source,Func<TValue,NavOwnedStorage> owner)
        {
            if(sealedBuilder)throw new InvalidOperationException("Sealed navigation builder.");
            if(source is NavigationCommandSlots<TKey,TValue> slots&&slots.UsesReferenceKeys!=referenceKeys)throw new ArgumentException("Navigation key identity mode differs.");
            var layout=NavigationCommandLayout.Capture(source);length=layout[0];if(length>Capacity)throw new ArgumentException("Prepared capacity is too small.");
            for(int i=1;i<layout.Length;i++)free[layout.Length-1-i]=layout[i];freeCount=layout.Length-1;
            for(int i=0;i<length;i++)live[i]=true;for(int i=0;i<freeCount;i++)live[free[i]]=false;
            int slot=0;foreach(var row in source){while(!live[slot])slot++;keys[slot]=row.Key;values[slot]=row.Value;Own(slot,owner?.Invoke(row.Value)??(source as NavigationCommandSlots<TKey,TValue>)?.OwnerFor(row.Key));int bucket=Bucket(row.Key);next[slot]=buckets[bucket];buckets[bucket]=slot;count++;slot++;}
        }
        internal bool TryReadSlot(int slot,out TKey key,out TValue value){bool found=slot>=0&&slot<length&&live[slot];key=found?keys[slot]:default;value=found?values[slot]:default;return found;}
        // Both concrete backings are already allocated. The caller holds the
        // live source read gate; no iterator, destination reserve or callback
        // is needed while copying its exact slots and free-stack order.
        internal void CopyPreparedFrom(NavigationCommandSlots<TKey,TValue> source)
        {
            if(source==null||ReferenceEquals(source,this)||sealedBuilder||length!=0||count!=0||freeCount!=0||source.referenceKeys!=referenceKeys||source.length>Capacity)throw new ArgumentException("Invalid concrete navigation slot copy.");RequireEdge(source);
            length=source.length;freeCount=source.freeCount;
            for(int i=0;i<freeCount;i++)free[i]=source.free[i];
            for(int i=0;i<length;i++)if(source.live[i]){keys[i]=source.keys[i];values[i]=source.values[i];live[i]=true;Own(i,source.edges[i]);int bucket=Bucket(keys[i]);next[i]=buckets[bucket];buckets[bucket]=i;count++;}
            version++;
        }
        internal NavOwnedStorage OwnerFor(TKey key){int slot=Find(key);return slot<0?null:edges[slot];}
        internal int FindOwnedSlot(TKey key,TValue value,NavOwnedStorage owner)
        {int slot=Find(key);return slot>=0&&ReferenceEquals(values[slot],value)&&ReferenceEquals(edges[slot],owner)?slot:-1;}
        // The caller prevalidates this live slot while holding its authority
        // gate. Its owning edge transfers to a typed retirement carrier;
        // destroying the edge is deliberately outside this operation.
        internal void ExtractPreparedNoThrow(int slot,out TValue value,out NavOwnedStorage edge)
        {
            int bucket=Bucket(keys[slot]),previous=-1;for(int i=buckets[bucket];i!=slot;i=next[i])previous=i;
            if(previous<0)buckets[bucket]=next[slot];else next[previous]=next[slot];value=values[slot];edge=edges[slot];
            edges[slot]=null;live[slot]=false;keys[slot]=default;values[slot]=default;free[freeCount++]=slot;count--;version++;
        }
        private void Own(int slot,NavOwnedStorage edge){if(edge!=null){RequireEdge(edge);edge.Retain();}edges[slot]?.Release();edges[slot]=edge;}
        private int Bucket(TKey key)=>(((referenceKeys?System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key):EqualityComparer<TKey>.Default.GetHashCode(key))&int.MaxValue)%Capacity);
        private bool SameKey(TKey a,TKey b)=>referenceKeys?ReferenceEquals(a,b):EqualityComparer<TKey>.Default.Equals(a,b);
        private int Find(TKey key){if(Capacity==0)return -1;for(int i=buckets[Bucket(key)];i>=0;i=next[i])if(SameKey(keys[i],key))return i;return -1;}
        internal void SetPreparing(TKey key,TValue value,NavOwnedStorage owner=null){if(sealedBuilder)throw new InvalidOperationException("Sealed navigation builder.");int slot=Find(key);if(slot<0){AddPreparing(key,value,owner);return;}Own(slot,owner);values[slot]=value;version++;}
        internal void AddPreparing(TKey key,TValue value,NavOwnedStorage owner=null){if(sealedBuilder||Find(key)>=0||freeCount==0&&length==Capacity)throw new InvalidOperationException("Invalid navigation slot birth.");int slot=freeCount>0?free[--freeCount]:length++;Own(slot,owner);keys[slot]=key;values[slot]=value;live[slot]=true;int bucket=Bucket(key);next[slot]=buckets[bucket];buckets[bucket]=slot;count++;version++;}
        internal void Seal(){if(count!=length-freeCount)throw new InvalidOperationException("Incomplete navigation slot layout.");sealedBuilder=true;}
        internal void CopyLayoutTo(int[] target){if(target==null||target.Length!=LayoutLength)throw new ArgumentException("Navigation layout destination shape changed.");target[0]=length;for(int i=0;i<freeCount;i++)target[i+1]=free[freeCount-1-i];}
        internal void CopyLayoutTo(int[] target,int offset)
        {if(target==null||offset<0||target.Length-offset<LayoutLength)throw new ArgumentException("Navigation layout destination is too small.");target[offset]=length;for(int i=0;i<freeCount;i++)target[offset+i+1]=free[freeCount-1-i];}
        internal void InitializeLayoutPreparing(int[] layout)
        {
            if(sealedBuilder||length!=0||count!=0||freeCount!=0||layout==null||layout.Length<1||layout[0]<0||layout[0]>Capacity||layout.Length-1>layout[0])throw new ArgumentException("Invalid empty navigation layout preparation.");
            length=layout[0];freeCount=layout.Length-1;
            for(int i=0;i<freeCount;i++){int slot=layout[i+1];if(slot<0||slot>=length||next[slot]==int.MinValue)throw new ArgumentException("Invalid prepared free slot.");next[slot]=int.MinValue;free[freeCount-1-i]=slot;}
        }
        internal bool IsPreparedHole(int slot)=>slot>=0&&slot<length&&next[slot]==int.MinValue;
        internal void AddAtPreparedSlot(int slot,TKey key,TValue value,NavOwnedStorage owner=null)
        {
            if(sealedBuilder||slot<0||slot>=length||live[slot]||IsPreparedHole(slot)||Find(key)>=0)throw new ArgumentException("Invalid prepared slot role.");
            Own(slot,owner);keys[slot]=key;values[slot]=value;live[slot]=true;int bucket=Bucket(key);next[slot]=buckets[bucket];buckets[bucket]=slot;count++;version++;
        }
        // Existing authority callers are migrated separately to prepared writes.
        // This adapter exposes observations only; publication owns the backing.
        public int Count=>count;public bool IsReadOnly=>false;
        public TValue this[TKey key]{get{int i=Find(key);if(i<0)throw new KeyNotFoundException();return values[i];}set{bool wasSealed=sealedBuilder;sealedBuilder=false;try{SetPreparing(key,value,(value as INavStorageFacade)?.StorageRoot);}finally{sealedBuilder=wasSealed;}}}
        public bool ContainsKey(TKey key)=>Find(key)>=0;
        public bool TryGetValue(TKey key,out TValue value){int i=Find(key);value=i<0?default:values[i];return i>=0;}
        public ICollection<TKey> Keys{get{var result=new TKey[count];int j=0;for(int i=0;i<length;i++)if(live[i])result[j++]=keys[i];return result;}}
        public ICollection<TValue> Values{get{var result=new TValue[count];int j=0;for(int i=0;i<length;i++)if(live[i])result[j++]=values[i];return result;}}
        public IEnumerator<KeyValuePair<TKey,TValue>> GetEnumerator(){int observed=version;for(int i=0;i<length;i++){if(version!=observed)throw new InvalidOperationException("Navigation backing changed.");if(live[i])yield return new KeyValuePair<TKey,TValue>(keys[i],values[i]);}}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        public void Add(TKey key,TValue value){bool wasSealed=sealedBuilder;sealedBuilder=false;try{AddPreparing(key,value,(value as INavStorageFacade)?.StorageRoot);}finally{sealedBuilder=wasSealed;}}
        public bool Remove(TKey key){int slot=Find(key);if(slot<0)return false;int bucket=Bucket(key),previous=-1;for(int i=buckets[bucket];i!=slot;i=next[i])previous=i;if(previous<0)buckets[bucket]=next[slot];else next[previous]=next[slot];live[slot]=false;keys[slot]=default;values[slot]=default;var edge=edges[slot];edges[slot]=null;free[freeCount++]=slot;count--;version++;edge?.Release();return true;}
        public void Clear(){for(int i=0;i<length;i++){edges[i]?.Release();edges[i]=null;keys[i]=default;values[i]=default;live[i]=false;}for(int i=0;i<buckets.Length;i++)buckets[i]=-1;length=count=freeCount=0;version++;}
        internal int[] CaptureLayout(){var result=new int[freeCount+1];result[0]=length;for(int i=0;i<freeCount;i++)result[i+1]=free[freeCount-1-i];return result;}
        internal void RestoreLayout(int[] layout){if(layout==null||layout.Length<1||layout[0]!=length||layout.Length!=freeCount+1)throw new ArgumentException("Prepared navigation layout cannot be rearranged live.");for(int i=0;i<freeCount;i++)if(layout[i+1]!=free[freeCount-1-i])throw new ArgumentException("Prepared navigation layout differs.");}
        public void Add(KeyValuePair<TKey,TValue> row)=>Add(row.Key,row.Value);public bool Remove(KeyValuePair<TKey,TValue> row)=>Contains(row)&&Remove(row.Key);
        public bool Contains(KeyValuePair<TKey,TValue> row)=>TryGetValue(row.Key,out var v)&&EqualityComparer<TValue>.Default.Equals(v,row.Value);
        public void CopyTo(KeyValuePair<TKey,TValue>[] target,int offset){foreach(var row in this)target[offset++]=row;}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){for(int i=0;i<edges.Length;i++){edges[i]?.Release();edges[i]=null;values[i]=default;}}
    }
}

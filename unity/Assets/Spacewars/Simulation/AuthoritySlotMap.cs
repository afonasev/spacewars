using System;
using System.Collections;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    // Explicit authority iteration/allocation state. This follows the current Unity
    // Dictionary entry-slot/LIFO removal semantics without reading framework internals.
    public sealed class AuthoritySlotMap<TKey,TValue> : IDictionary<TKey,TValue>, IDictionary
    {
        // One layout header plus holes must fit the 262144-item wire array bound.
        public const int MaximumSlots=262143;
        private struct Slot { public bool Live; public TKey Key; public TValue Value; }
        private readonly List<Slot> slots=new List<Slot>();
        private readonly Stack<int> free=new Stack<int>();
        private readonly Dictionary<TKey,int> index=new Dictionary<TKey,int>(); // derived lookup
        private int version;
        private ICollection<TKey> keyView;private ValueCollection valueView;
        public int Count=>index.Count;
        internal int SlotCount=>slots.Count;
        internal bool TryReadSlot(int slot,out TKey key,out TValue value)
        {if(slot>=0&&slot<slots.Count&&slots[slot].Live){key=slots[slot].Key;value=slots[slot].Value;return true;}key=default;value=default;return false;}
        public TValue this[TKey key] { get=>slots[index[key]].Value; set { if(index.TryGetValue(key,out var i)){var s=slots[i];s.Value=value;slots[i]=s;version++;}else Add(key,value); } }
        public void Add(TKey key,TValue value){if(index.ContainsKey(key))throw new ArgumentException("Duplicate key.");int i=free.Count>0?free.Pop():slots.Count;index.Add(key,i);var s=new Slot{Live=true,Key=key,Value=value};if(i==slots.Count)slots.Add(s);else slots[i]=s;version++;}
        public bool Remove(TKey key){if(!index.TryGetValue(key,out var i))return false;index.Remove(key);slots[i]=default;free.Push(i);version++;return true;}
        public void Clear(){slots.Clear();free.Clear();index.Clear();version++;}
        public bool ContainsKey(TKey key)=>index.ContainsKey(key);
        public bool TryGetValue(TKey key,out TValue value){if(index.TryGetValue(key,out var i)){value=slots[i].Value;return true;}value=default;return false;}
        public IEnumerator<KeyValuePair<TKey,TValue>> GetEnumerator(){int v=version;for(int i=0;i<slots.Count;i++){if(v!=version)throw new InvalidOperationException("Collection changed.");var s=slots[i];if(s.Live)yield return new KeyValuePair<TKey,TValue>(s.Key,s.Value);}if(v!=version)throw new InvalidOperationException("Collection changed.");}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        public ICollection<TKey> Keys=>keyView??(keyView=new View<TKey>(this,p=>p.Key));
        public ValueCollection Values=>valueView??(valueView=new ValueCollection(this));
        ICollection<TValue> IDictionary<TKey,TValue>.Values=>Values;
        public bool IsReadOnly=>false;
        public void Add(KeyValuePair<TKey,TValue> p)=>Add(p.Key,p.Value);
        public bool Contains(KeyValuePair<TKey,TValue> p)=>TryGetValue(p.Key,out var v)&&EqualityComparer<TValue>.Default.Equals(v,p.Value);
        public bool Remove(KeyValuePair<TKey,TValue> p)=>Contains(p)&&Remove(p.Key);
        public void CopyTo(KeyValuePair<TKey,TValue>[] array,int offset){foreach(var p in this)array[offset++]=p;}
        // The bound protects decoded allocation, not gameplay balance. Live rows are
        // emitted in occupied-slot order; the complement of free slots binds them.
        internal int LayoutLength=>free.Count+1;
        public int[] CaptureLayout(){var a=new int[free.Count+1];a[0]=slots.Count;free.CopyTo(a,1);return a;}
        public void RestoreLayout(int[] layout){
            if(layout==null||layout.Length<1||layout[0]<0||layout[0]>MaximumSlots||layout.Length-1>layout[0]||Count!=layout[0]-(layout.Length-1))throw new ArgumentException("Invalid authority layout.");
            var holes=new HashSet<int>();for(int i=1;i<layout.Length;i++)if(layout[i]<0||layout[i]>=layout[0]||!holes.Add(layout[i]))throw new ArgumentException("Invalid free slot.");
            var live=new List<Slot>();foreach(var s in slots)if(s.Live)live.Add(s);
            slots.Clear();index.Clear();free.Clear();int row=0;
            for(int i=0;i<layout[0];i++){if(holes.Contains(i))slots.Add(default);else {var s=live[row++];slots.Add(s);index.Add(s.Key,i);}}
            for(int i=layout.Length-1;i>0;i--)free.Push(layout[i]);version++;
        }
        // Values are scanned throughout authority projection. The concrete path
        // reads occupied slots directly, without two iterator objects or a selector
        // per scan. Interface callers retain the same read-only live collection.
        public sealed class ValueCollection : ICollection<TValue>, ICollection
        {
            private readonly AuthoritySlotMap<TKey,TValue> map;
            internal ValueCollection(AuthoritySlotMap<TKey,TValue> map){this.map=map;}
            public int Count=>map.Count;public bool IsReadOnly=>true;
            public ValueEnumerator GetEnumerator()=>new ValueEnumerator(map);
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()=>GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
            public bool Contains(TValue value){foreach(var item in this)if(EqualityComparer<TValue>.Default.Equals(item,value))return true;return false;}
            public void CopyTo(TValue[] array,int offset){foreach(var value in this)array[offset++]=value;}
            public void Add(TValue value)=>throw new NotSupportedException();public bool Remove(TValue value)=>throw new NotSupportedException();public void Clear()=>throw new NotSupportedException();
            bool ICollection.IsSynchronized=>false;object ICollection.SyncRoot=>map;
            void ICollection.CopyTo(Array array,int offset){foreach(var value in this)array.SetValue(value,offset++);}
        }
        public struct ValueEnumerator : IEnumerator<TValue>
        {
            private readonly AuthoritySlotMap<TKey,TValue> map;
            private int next,observedVersion;private bool started,finished;private TValue current;
            internal ValueEnumerator(AuthoritySlotMap<TKey,TValue> map){this.map=map;next=observedVersion=0;started=finished=false;current=default;}
            public TValue Current=>current;object IEnumerator.Current=>current;
            public bool MoveNext()
            {
                if(finished)return false;
                // Match the old yield iterator: capture at first MoveNext, and
                // reject mutations between moves, including after its last value.
                if(!started){observedVersion=map.version;started=true;}
                if(observedVersion!=map.version){finished=true;throw new InvalidOperationException("Collection changed.");}
                while(next<map.slots.Count){int slot=next++;if(!map.slots[slot].Live)continue;current=map.slots[slot].Value;return true;}
                finished=true;return false;
            }
            public void Dispose(){finished=true;}
            public void Reset()=>throw new NotSupportedException();
        }
        private sealed class View<T> : ICollection<T>, ICollection
        {
            private readonly AuthoritySlotMap<TKey,TValue> map;private readonly Func<KeyValuePair<TKey,TValue>,T> select;
            public View(AuthoritySlotMap<TKey,TValue> map,Func<KeyValuePair<TKey,TValue>,T> select){this.map=map;this.select=select;}
            public int Count=>map.Count;public bool IsReadOnly=>true;
            public IEnumerator<T> GetEnumerator(){foreach(var p in map)yield return select(p);}IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
            public bool Contains(T value){foreach(var item in this)if(EqualityComparer<T>.Default.Equals(item,value))return true;return false;}
            public void CopyTo(T[] array,int offset){foreach(var v in this)array[offset++]=v;}
            public void Add(T value)=>throw new NotSupportedException();public bool Remove(T value)=>throw new NotSupportedException();public void Clear()=>throw new NotSupportedException();
            bool ICollection.IsSynchronized=>false;object ICollection.SyncRoot=>map;void ICollection.CopyTo(Array array,int offset){foreach(var v in this)array.SetValue(v,offset++);}
        }
        object IDictionary.this[object key]{get=>key is TKey k&&TryGetValue(k,out var value)?(object)value:null;set=>this[(TKey)key]=(TValue)value;}
        bool IDictionary.IsFixedSize=>false;bool IDictionary.IsReadOnly=>false;
        ICollection IDictionary.Keys=>(ICollection)Keys;ICollection IDictionary.Values=>(ICollection)Values;
        void IDictionary.Add(object key,object value)=>Add((TKey)key,(TValue)value);
        bool IDictionary.Contains(object key)=>key is TKey k&&ContainsKey(k);
        void IDictionary.Remove(object key){if(key is TKey k)Remove(k);}
        IDictionaryEnumerator IDictionary.GetEnumerator()=>new DictionaryEnumerator(GetEnumerator());
        bool ICollection.IsSynchronized=>false;object ICollection.SyncRoot=>this;
        void ICollection.CopyTo(Array array,int offset){foreach(var p in this)array.SetValue(new DictionaryEntry(p.Key,p.Value),offset++);}
        private sealed class DictionaryEnumerator : IDictionaryEnumerator
        {
            private readonly IEnumerator<KeyValuePair<TKey,TValue>> rows;public DictionaryEnumerator(IEnumerator<KeyValuePair<TKey,TValue>> rows){this.rows=rows;}
            public DictionaryEntry Entry=>new DictionaryEntry(rows.Current.Key,rows.Current.Value);public object Key=>rows.Current.Key;public object Value=>rows.Current.Value;public object Current=>Entry;
            public bool MoveNext()=>rows.MoveNext();public void Reset()=>rows.Reset();
        }
    }
}

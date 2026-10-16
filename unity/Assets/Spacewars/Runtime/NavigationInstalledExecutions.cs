using System;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // One paid facade over actor-owned edges. Crowd supplies entity lookup and
    // ordered traversal; there is no second installed index or spare capacity.
    internal sealed class NavigationInstalledExecutions : NavOwnedStorage,IDictionary<int,NavigationInstalledExecutionState>
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationInstalledExecutions))+NavStorageLayout.ObjectBytesBound(typeof(KeyView))+NavStorageLayout.ObjectBytesBound(typeof(ValueView));
        private NavCrowd crowd;private readonly KeyView keys;private readonly ValueView values;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationInstalledExecutions(NavCrowd crowd):base(Layout){this.crowd=crowd;keys=new KeyView(this);values=new ValueView(this);Retain();}
        internal static NavigationInstalledExecutions CreateOwned(NavCrowd crowd,INavStorageReservation reservation)
        {using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout);return allocation.Register(new NavigationInstalledExecutions(crowd));}}
        private static NavigationInstalledExecutionState State(NavUnit actor)
            =>(actor.StorageOwner.InstalledExecution as NavigationInstalledExecutionOwner)?.State??actor.StorageOwner.LegacyInstalledExecution as NavigationInstalledExecutionState;
        public int Count{get{int count=0;if(crowd!=null)for(int i=0;i<crowd.Units.Count;i++)if(State(crowd.Units[i])!=null)count++;return count;}}
        public bool IsReadOnly=>false;
        internal bool IsDetached(int entity)=>crowd.TryGet(entity,out var actor)&&actor.StorageOwner.InstalledDetached;
        internal void SetDetachedNoThrow(int entity,bool detached){if(crowd.TryGet(entity,out var actor)){actor.StorageOwner.InstalledDetached=detached;if(actor.StorageOwner.LegacyInstalledExecution is NavigationInstalledExecutionState legacy)legacy.Detached=detached;}}
        internal NavigationInstalledExecutionOwner OwnerFor(int entity)=>crowd.TryGet(entity,out var actor)?actor.StorageOwner.InstalledExecution as NavigationInstalledExecutionOwner:null;
        public bool TryGetValue(int key,out NavigationInstalledExecutionState value){value=crowd.TryGet(key,out var actor)?State(actor):null;return value!=null;}
        public bool ContainsKey(int key)=>TryGetValue(key,out _);
        public NavigationInstalledExecutionState this[int key]{get{if(!TryGetValue(key,out var value))throw new KeyNotFoundException();return value;}set=>Set(key,value,value.Detached);}
        internal void Set(int key,NavigationInstalledExecutionState value,bool detached)
        {
            if(!crowd.TryGet(key,out var actor)||value==null||value.Entity!=key)throw new ArgumentException("Installed actor/entity mismatch.");
            var owner=value.StorageOwner;if(owner!=null){actor.StorageOwner.RequireEdge(owner);owner.Retain();}
            actor.StorageOwner.SetInstalledNoThrow(owner,detached,owner==null?value:null,out var retired);retired?.Release();
        }
        public void Add(int key,NavigationInstalledExecutionState value){if(ContainsKey(key))throw new ArgumentException("Duplicate installed actor.");Set(key,value,value.Detached);}
        public bool Remove(int key){if(!crowd.TryGet(key,out var actor)||State(actor)==null)return false;actor.StorageOwner.SetInstalledNoThrow(null,false,null,out var retired);retired?.Release();return true;}
        public void Clear(){if(crowd!=null)for(int i=0;i<crowd.Units.Count;i++){crowd.Units[i].StorageOwner.SetInstalledNoThrow(null,false,null,out var retired);retired?.Release();}}
        public KeyView Keys=>keys;public ValueView Values=>values;
        ICollection<int> IDictionary<int,NavigationInstalledExecutionState>.Keys=>keys;
        ICollection<NavigationInstalledExecutionState> IDictionary<int,NavigationInstalledExecutionState>.Values=>values;
        public struct Enumerator : IEnumerator<KeyValuePair<int,NavigationInstalledExecutionState>>
        {
            private readonly NavigationInstalledExecutions owner;private int index;private KeyValuePair<int,NavigationInstalledExecutionState> current;
            internal Enumerator(NavigationInstalledExecutions owner){this.owner=owner;index=0;current=default;}
            public KeyValuePair<int,NavigationInstalledExecutionState> Current=>current;object IEnumerator.Current=>current;
            public bool MoveNext(){if(owner.crowd!=null)while(index<owner.crowd.Units.Count){var actor=owner.crowd.Units[index++];var state=State(actor);if(state!=null){current=new KeyValuePair<int,NavigationInstalledExecutionState>(actor.Id,state);return true;}}return false;}
            public void Reset(){index=0;current=default;}public void Dispose(){}
        }
        public Enumerator GetEnumerator()=>new Enumerator(this);
        IEnumerator<KeyValuePair<int,NavigationInstalledExecutionState>> IEnumerable<KeyValuePair<int,NavigationInstalledExecutionState>>.GetEnumerator()=>GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        public void Add(KeyValuePair<int,NavigationInstalledExecutionState> item)=>Add(item.Key,item.Value);
        public bool Contains(KeyValuePair<int,NavigationInstalledExecutionState> item)=>TryGetValue(item.Key,out var value)&&ReferenceEquals(value,item.Value);
        public bool Remove(KeyValuePair<int,NavigationInstalledExecutionState> item)=>Contains(item)&&Remove(item.Key);
        public void CopyTo(KeyValuePair<int,NavigationInstalledExecutionState>[] array,int index){foreach(var item in this)array[index++]=item;}
        internal sealed class KeyView : ICollection<int>
        {
            private readonly NavigationInstalledExecutions owner;internal KeyView(NavigationInstalledExecutions owner){this.owner=owner;}
            public int Count=>owner.Count;public bool IsReadOnly=>true;public bool Contains(int item)=>owner.ContainsKey(item);
            public void CopyTo(int[] array,int index){foreach(var item in owner)array[index++]=item.Key;}
            public IEnumerator<int> GetEnumerator(){foreach(var item in owner)yield return item.Key;}IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
            public void Add(int item)=>throw new NotSupportedException();public void Clear()=>throw new NotSupportedException();public bool Remove(int item)=>throw new NotSupportedException();
        }
        internal sealed class ValueView : ICollection<NavigationInstalledExecutionState>
        {
            private readonly NavigationInstalledExecutions owner;internal ValueView(NavigationInstalledExecutions owner){this.owner=owner;}
            public int Count=>owner.Count;public bool IsReadOnly=>true;
            public bool Contains(NavigationInstalledExecutionState item)=>item!=null&&owner.TryGetValue(item.Entity,out var value)&&ReferenceEquals(value,item);
            public void CopyTo(NavigationInstalledExecutionState[] array,int index){foreach(var item in owner)array[index++]=item.Value;}
            public struct Enumerator : IEnumerator<NavigationInstalledExecutionState>
            {
                private NavigationInstalledExecutions.Enumerator entries;internal Enumerator(NavigationInstalledExecutions owner){entries=owner.GetEnumerator();}
                public NavigationInstalledExecutionState Current=>entries.Current.Value;object IEnumerator.Current=>Current;
                public bool MoveNext()=>entries.MoveNext();public void Reset()=>entries.Reset();public void Dispose(){}
            }
            public Enumerator GetEnumerator()=>new Enumerator(owner);
            IEnumerator<NavigationInstalledExecutionState> IEnumerable<NavigationInstalledExecutionState>.GetEnumerator()=>GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
            public void Add(NavigationInstalledExecutionState item)=>throw new NotSupportedException();public void Clear()=>throw new NotSupportedException();public bool Remove(NavigationInstalledExecutionState item)=>throw new NotSupportedException();
        }
        protected override void ReleaseEdges(){crowd=null;}
    }
    public sealed partial class NavigationSession
    {
        private void InitializeInstalledExecutions()
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,0,out var scope))throw new InvalidOperationException("Installed facade admission refused.");
            using(scope){var value=NavigationInstalledExecutions.CreateOwned(Crowd,scope.OwnedReservation());try{scope.Attach(value);scope.Commit(value,_=>installedExecutions=value);}catch{value.Release();throw;}}
        }
    }
}

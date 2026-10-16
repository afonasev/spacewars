using System;

namespace Spacewars.Simulation
{
    // Stable content hash chooses a bucket, but identity inside a bucket is
    // physical reference equality. Equal authored bindings never merge owners.
    // Buckets, entries and their numeric links are persistent owned storage.
    internal sealed partial class NavProviderReferences : NavOwnedStorage
    {
        internal sealed partial class Entry : NavOwnedStorage
        {
            internal readonly ILayeredNavigationProvider Provider;
            internal readonly int Count;
            internal readonly long DiagnosticBytes;
            private NavOwnedStorage providerRoot;
            internal Entry(ILayeredNavigationProvider provider,int count,long bytes):base(128)
            {Provider=provider;Count=count;DiagnosticBytes=bytes;}
            internal void Acquire(){var root=(Provider as INavStorageFacade)?.StorageRoot;if(IsRegistered)RequireEdge(root);root?.Retain();providerRoot=root;}
            protected override void ReleaseEdges()=>providerRoot?.Release();
        }
        private NavCowDictionary<uint,NavPersistentQueue<Entry>> buckets;
        private Func<long,bool> reserve;
        private bool frozen,released,faulted;
        internal int Count{get;private set;}
        internal long DiagnosticBytes{get;private set;}
        internal static NavProviderReferences Create(Func<long,bool> reserve=null)
        {using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(128);return new NavProviderReferences(reserve,true);}}
        internal NavProviderReferences(Func<long,bool> reserve=null,bool alreadyReserved=false):base(128)
        {
            this.reserve=reserve;
            using(var a=new NavStorageAllocation(reserve)){a.Reserve(128);a.Register(this);Retain();}
            try{buckets=NavCowDictionary<uint,NavPersistentQueue<Entry>>.Create(insertionOrder:false,reserve:reserve);}catch{ReleaseStorage();throw;}
        }
        private static NavProviderReferences Create(NavProviderReferences source,Func<long,bool> reserve)
        {using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(128);return new NavProviderReferences(source,reserve,true);}}
        private NavProviderReferences(NavProviderReferences source,Func<long,bool> reserve,bool alreadyReserved=false):base(128)
        {
            this.reserve=reserve;
            using(var a=new NavStorageAllocation(reserve)){if(!alreadyReserved)a.Reserve(128);a.Register(this);Retain();}
            try{buckets=source.buckets.ForkPrepared(reserve);Count=source.Count;DiagnosticBytes=source.DiagnosticBytes;}catch{ReleaseStorage();throw;}
        }
        internal static NavProviderReferences CreateOwned(INavStorageReservation reservation)
        {if(reservation==null)throw new ArgumentNullException(nameof(reservation));return NavProviderReferences.Create(reservation.TryReserve);}
        internal NavProviderReferences ForkOwned(INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,this);return ForkPrepared(reservation.TryReserve);}
        internal NavProviderReferences ForkPrepared(Func<long,bool> admission)
        {
            if(!frozen||faulted||admission==null)throw new InvalidOperationException("Seal provider index before preparing a fork.");
            if(IsRegistered){if(!(admission.Target is INavStorageReservation owner))throw new ArgumentException("Missing provider ledger.");NavStorageAdmission.RequireOwned(owner,this);}
            return NavProviderReferences.Create(this,admission);
        }
        internal void Seal(){if(faulted)throw new InvalidOperationException("Failed provider index must be discarded.");buckets.Seal();reserve=null;frozen=true;}
        private static uint Hash(ILayeredNavigationProvider provider)
        {
            string key=provider.Binding;if(key==null||key.Length!=64)throw new ArgumentException("Invalid provider binding.");uint result=0;
            for(int n=0;n<8;n++){char c=key[n];int value=c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:-1;if(value<0)throw new ArgumentException("Invalid provider binding.");result=(result<<4)|(uint)value;}return result;
        }
        private static long Find(NavPersistentQueue<Entry> bucket,ILayeredNavigationProvider provider)
        {foreach(var item in bucket.Entries)if(ReferenceEquals(item.Value.Provider,provider))return item.Key;return 0;}
        internal bool Contains(ILayeredNavigationProvider provider)
        {if(provider==null)throw new ArgumentNullException(nameof(provider));return buckets.TryGetValue(Hash(provider),out var bucket)&&Find(bucket,provider)!=0;}
        private Entry Make(ILayeredNavigationProvider provider,int count,long bytes)
        {using(var a=new NavStorageAllocation(reserve)){a.Reserve(128);var entry=a.Register(new Entry(provider,count,bytes));entry.Acquire();entry.Retain();return entry;}}
        private void Mutable(){if(frozen||faulted||released)throw new InvalidOperationException("Committed provider index cannot be mutated.");}
        internal void Add(ILayeredNavigationProvider provider,long bytes)
        {
            Mutable();if(provider==null||bytes<0)throw new ArgumentException("Invalid provider reference.");if(IsRegistered)RequireEdge((provider as INavStorageFacade)?.StorageRoot);
            uint hash=Hash(provider);buckets.TryGetValue(hash,out var old);long id=old==null?0:Find(old,provider);
            NavPersistentQueue<Entry> next=null;Entry entry=null;
            try{
                next=old==null?NavPersistentQueue<Entry>.Create(reserve):old.ForkPrepared(reserve??LocalAdmission);
                entry=Make(provider,id==0?1:checked(old[id].Count+1),id==0?bytes:old[id].DiagnosticBytes);
                if(id==0)next.AddLast(entry);else next.Replace(id,entry);next.Seal();buckets[hash]=next;
                if(id==0){Count++;DiagnosticBytes=checked(DiagnosticBytes+bytes);}
            }catch{faulted=true;throw;}finally{entry?.Release();next?.ReleaseStorage();}
        }
        internal void Remove(ILayeredNavigationProvider provider)
        {
            Mutable();uint hash=Hash(provider);if(!buckets.TryGetValue(hash,out var old))throw new ArgumentException("Unknown provider reference.");long id=Find(old,provider);if(id==0)throw new ArgumentException("Unknown provider reference.");
            NavPersistentQueue<Entry> next=null;Entry entry=null;var prior=old[id];
            try{
                next=old.ForkPrepared(reserve??LocalAdmission);
                if(prior.Count==1)next.Remove(id);else{entry=Make(provider,prior.Count-1,prior.DiagnosticBytes);next.Replace(id,entry);}
                next.Seal();if(next.Count==0)buckets.Remove(hash);else buckets[hash]=next;
                if(prior.Count==1){Count--;DiagnosticBytes-=prior.DiagnosticBytes;}
            }catch{faulted=true;throw;}finally{entry?.Release();next?.ReleaseStorage();}
        }
        internal void Clear(){Mutable();var old=buckets;buckets=NavCowDictionary<uint,NavPersistentQueue<Entry>>.Create(insertionOrder:false,reserve:reserve);old.Release();Count=0;DiagnosticBytes=0;}
        private static readonly Func<long,bool> LocalAdmission=_=>true;
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges()=>buckets?.Release();
    }
}

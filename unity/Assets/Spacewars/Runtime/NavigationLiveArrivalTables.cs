using System;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal sealed class NavigationLiveArrivalProvider : NavOwnedStorage
        {
            private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveArrivalProvider))+NavStorageLayout.ObjectBytesBound(typeof(ArrivalProviderRecord));
            internal ArrivalProviderRecord Record;
            internal NavDeclarationStorage ProviderRoot;
            internal int Revision;
            internal NavigationLiveArrivalProvider RetiredNext;
            internal int RetiredIndexReferences;
            internal NavigationCommandSlots<ILayeredNavigationProvider,ArrivalProviderRecord> RetiredReferenceMap;
            internal NavigationCommandSlots<long,ArrivalProviderRecord> RetiredIdMap;
            private bool released;
            internal override bool IsImmutableCaptureRoot=>false;
            private NavigationLiveArrivalProvider():base(Layout){Retain();}
            internal static NavigationLiveArrivalProvider CreateOwned(long id,int references,ILayeredNavigationProvider provider,INavStorageReservation reservation)
            {
                var root=(provider as INavStorageFacade)?.StorageRoot as NavDeclarationStorage;
                if(id<1||references<1||root==null||!root.IsImmutableCaptureRoot||!ReferenceEquals(root.Facade,provider))throw new ArgumentException("Invalid live provider record input.");NavStorageAdmission.RequireOwned(reservation,root);
                using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                    allocation.Reserve(Layout);var owner=allocation.Register(new NavigationLiveArrivalProvider());
                    try{owner.ProviderRoot=NavPhysicalMacro.Acquire(owner,root);owner.Record=new ArrivalProviderRecord{StorageOwner=owner,Id=id,References=references,Provider=provider};return owner;}
                    catch{owner.ReleaseStorage();throw;}
                }
            }
            internal void SetReferencesNoThrow(int value){Record.References=value;Revision++;}
            internal void ReleaseStorage(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>ReleaseStorage();
            protected override void ReleaseEdges(){RetiredReferenceMap?.Release();RetiredIdMap?.Release();RetiredReferenceMap=null;RetiredIdMap=null;ProviderRoot?.Release();ProviderRoot=null;Record=null;}
        }
        // Runtime-only authority. Empty provider indexes are retained from
        // construction, so final owner removal never asks admission for memory.
        internal sealed class NavigationLiveArrivalTables : NavOwnedStorage
        {
            private static readonly long Header=NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveArrivalTables))+NavStorageLayout.ObjectBytesBound(typeof(AttemptOrder))+NavStorageLayout.ObjectBytesBound(typeof(OrderedValues<long>))+NavStorageLayout.ObjectBytesBound(typeof(OrderedValues<ArrivalAttempt>));
            private static long Layout(int capacity)=>checked(Header+32L+8L*capacity);
            internal NavigationCommandSlots<long,ArrivalAttempt> Attempts;
            internal NavigationCommandSlots<ILayeredNavigationProvider,ArrivalProviderRecord> References;
            internal NavigationCommandSlots<long,ArrivalProviderRecord> Ids;
            internal NavigationCommandSlots<ILayeredNavigationProvider,ArrivalProviderRecord> EmptyReferences,ResetReferences;
            internal NavigationCommandSlots<long,ArrivalProviderRecord> EmptyIds,ResetIds;
            private readonly long[] sortedIds;
            internal readonly AttemptOrder Ordered;
            internal int Revision;
            private bool released,sealedRoot;
            internal override bool IsImmutableCaptureRoot=>false;
            internal int Count=>Attempts.Count;
            internal long SortedId(int index)=>sortedIds[index];
            private NavigationLiveArrivalTables(int capacity):base(Layout(capacity)){sortedIds=new long[capacity];Ordered=new AttemptOrder(this);Retain();}
            internal static NavigationLiveArrivalTables AllocatePrepared(int attempts,int providers,INavStorageReservation reservation,NavigationLiveArrivalTables source=null,int idCapacity=-1)
            {
                if(idCapacity<0)idCapacity=providers;
                if(idCapacity<0||idCapacity>65536||(providers==0)!=(idCapacity==0)||attempts<0||providers<0||attempts>AuthoritySlotMap<long,int>.MaximumSlots||providers>65536)throw new ArgumentException("Invalid live arrival table shape.");
                if(source!=null)NavStorageAdmission.RequireOwned(reservation,source);
                using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                    allocation.Reserve(Layout(attempts));var root=allocation.Register(new NavigationLiveArrivalTables(attempts));
                    try{
                        root.EmptyReferences=source==null?NavigationCommandSlots<ILayeredNavigationProvider,ArrivalProviderRecord>.CreateEmptyOwned(reservation,true):NavPhysicalMacro.Acquire(root,source.EmptyReferences);
                        root.EmptyIds=source==null?NavigationCommandSlots<long,ArrivalProviderRecord>.CreateEmptyOwned(reservation):NavPhysicalMacro.Acquire(root,source.EmptyIds);
                        root.EmptyReferences.Seal();root.EmptyIds.Seal();
                        root.Attempts=attempts==0?NavigationCommandSlots<long,ArrivalAttempt>.CreateEmptyOwned(reservation):NavigationCommandSlots<long,ArrivalAttempt>.CreateOwned(attempts,reservation);
                        if(providers==0){root.References=NavPhysicalMacro.Acquire(root,root.EmptyReferences);root.Ids=NavPhysicalMacro.Acquire(root,root.EmptyIds);}
                        else{
                            root.References=NavigationCommandSlots<ILayeredNavigationProvider,ArrivalProviderRecord>.CreateOwned(providers,reservation,true);root.Ids=NavigationCommandSlots<long,ArrivalProviderRecord>.CreateOwned(idCapacity,reservation);
                            root.ResetReferences=NavPhysicalMacro.Acquire(root,root.EmptyReferences);root.ResetIds=NavPhysicalMacro.Acquire(root,root.EmptyIds);
                        }
                        return root;
                    }catch{root.ReleaseStorage();throw;}
                }
            }
            // Destination arrays exist before the caller enters its read gate.
            internal void CopySourceObserved(NavigationLiveArrivalTables source)
            {
                if(sealedRoot||Count!=0||source==null||Attempts.Capacity<source.Attempts.Capacity||References.Capacity<source.References.Capacity||Ids.Capacity<source.Ids.Capacity)throw new ArgumentException("Live arrival source shape changed.");
                Attempts.CopyPreparedFrom(source.Attempts);
                if(References.Capacity!=0){References.CopyPreparedFrom(source.References);Ids.CopyPreparedFrom(source.Ids);}
                for(int i=0;i<source.Count;i++)sortedIds[i]=source.sortedIds[i];Revision=source.Revision;
            }
            internal void AddProviderPreparing(NavigationLiveArrivalProvider provider)
            {
                if(sealedRoot||provider==null||References.ContainsKey(provider.Record.Provider)||Ids.ContainsKey(provider.Record.Id))throw new ArgumentException("Duplicate live arrival provider.");
                References.AddPreparing(provider.Record.Provider,provider.Record,provider);Ids.AddPreparing(provider.Record.Id,provider.Record,provider);Revision++;
            }
            internal void AddAttemptPreparing(NavigationLiveArrivalAttempt attempt)
            {
                if(sealedRoot||attempt==null||Count!=0&&sortedIds[Count-1]>=attempt.Record.Id)throw new ArgumentException("Arrival IDs must be born monotonically.");
                int at=Count;Attempts.AddPreparing(attempt.Record.Id,attempt.Record,attempt);sortedIds[at]=attempt.Record.Id;Revision++;
            }
            internal void RemoveAttemptPreparing(ArrivalAttempt attempt)
            {
                if(sealedRoot||!CanExtractAttempt(attempt,out int slot,out int sorted))throw new ArgumentException("Missing projected arrival attempt.");
                var owner=ExtractAttemptNoThrow(slot,sorted);owner.Release();
            }
            internal void IncrementProviderPreparing(ILayeredNavigationProvider provider,INavStorageReservation r)
            {
                if(sealedRoot||!References.TryGetValue(provider,out var record))throw new ArgumentException("Missing projected provider owner.");
                var replacement=NavigationLiveArrivalProvider.CreateOwned(record.Id,checked(record.References+1),provider,r);try{References.SetPreparing(provider,replacement.Record,replacement);Ids.SetPreparing(record.Id,replacement.Record,replacement);}finally{replacement.ReleaseStorage();}Revision++;
            }
            internal void DecrementProviderPreparing(ILayeredNavigationProvider provider,INavStorageReservation r)
            {
                if(sealedRoot||!References.TryGetValue(provider,out var record)||record.References<1)throw new ArgumentException("Missing projected provider owner.");
                if(record.References==1){References.Remove(provider);Ids.Remove(record.Id);}
                else{var replacement=NavigationLiveArrivalProvider.CreateOwned(record.Id,record.References-1,provider,r);try{References.SetPreparing(provider,replacement.Record,replacement);Ids.SetPreparing(record.Id,replacement.Record,replacement);}finally{replacement.ReleaseStorage();}}
                Revision++;
            }
            internal void ResetEmptyProvidersPreparing()
            {
                if(sealedRoot||Ids.Count!=0||References.Count!=0)throw new ArgumentException("Projected provider indexes are not empty.");
                if(References.Capacity==0)return;var references=References;var ids=Ids;References=ResetReferences;Ids=ResetIds;ResetReferences=null;ResetIds=null;references.Release();ids.Release();Revision++;
            }
            internal void InitializeCatalogLayout(NavigationArrivalCatalog catalog)
            {
                catalog.InitializeLayout(0,Attempts);if(References.Capacity!=0)catalog.InitializeLayout(1,References);if(Ids.Capacity!=0)catalog.InitializeLayout(2,Ids);
                for(int i=0;i<catalog.AttemptCount;i++)sortedIds[i]=catalog.SortedAttempt(i);
            }
            internal void SealPrepared(){Attempts.Seal();References.Seal();Ids.Seal();sealedRoot=true;}
            internal bool TryProviderSlots(ArrivalProviderRecord record,out int reference,out int id)
            {
                var owner=record?.StorageOwner;reference=owner==null?-1:References.FindOwnedSlot(record.Provider,record,owner);id=owner==null?-1:Ids.FindOwnedSlot(record.Id,record,owner);
                return reference>=0&&id>=0&&record.References>0&&owner.RetiredIndexReferences==0;
            }
            // Both indexes and the final reset have been validated before entry.
            // Each removed slot edge transfers once to the same typed node.
            internal NavigationLiveArrivalProvider ExtractProviderNoThrow(ArrivalProviderRecord record,int reference,int id)
            {
                var owner=record.StorageOwner;References.ExtractPreparedNoThrow(reference,out _,out _);Ids.ExtractPreparedNoThrow(id,out _,out _);owner.SetReferencesNoThrow(0);owner.RetiredIndexReferences=2;
                if(Ids.Count==0){owner.RetiredReferenceMap=References;owner.RetiredIdMap=Ids;References=ResetReferences;Ids=ResetIds;ResetReferences=null;ResetIds=null;}
                Revision++;return owner;
            }
            internal bool CanExtractProvider(ArrivalProviderRecord record,out int reference,out int id)
                =>TryProviderSlots(record,out reference,out id)&&record.References==1&&(Ids.Count!=1||ResetReferences!=null&&ResetIds!=null&&ResetReferences.Count==0&&ResetIds.Count==0);
            internal void ChangeProviderReferencesNoThrow(ArrivalProviderRecord record,int value){record.StorageOwner.SetReferencesNoThrow(value);Revision++;}
            internal bool CanExtractAttempt(ArrivalAttempt record,out int slot,out int sorted)
            {
                slot=record?.StorageOwner==null?-1:Attempts.FindOwnedSlot(record.Id,record,record.StorageOwner);sorted=record==null?-1:System.Array.BinarySearch(sortedIds,0,Count,record.Id);return slot>=0&&sorted>=0;
            }
            internal NavigationLiveArrivalAttempt ExtractAttemptNoThrow(int slot,int sorted)
            {
                int count=Count;Attempts.ExtractPreparedNoThrow(slot,out var record,out _);for(int i=sorted+1;i<count;i++)sortedIds[i-1]=sortedIds[i];sortedIds[count-1]=0;Revision++;return record.StorageOwner;
            }
            internal sealed class OrderedValues<T> : ICollection<T>
            {
                private readonly NavigationLiveArrivalTables root;private readonly bool values;
                internal OrderedValues(NavigationLiveArrivalTables root,bool values){this.root=root;this.values=values;}
                public int Count=>root.Count;public bool IsReadOnly=>true;
                public IEnumerator<T> GetEnumerator(){for(int i=0;i<root.Count;i++){long id=root.sortedIds[i];yield return values?(T)(object)root.Attempts[id]:(T)(object)id;}}
                IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
                public bool Contains(T value){foreach(var item in this)if(EqualityComparer<T>.Default.Equals(item,value))return true;return false;}
                public void CopyTo(T[] array,int offset){foreach(var value in this)array[offset++]=value;}
                public void Add(T value)=>throw new NotSupportedException();public bool Remove(T value)=>throw new NotSupportedException();public void Clear()=>throw new NotSupportedException();
            }
            internal sealed class AttemptOrder : IDictionary<long,ArrivalAttempt>
            {
                private readonly NavigationLiveArrivalTables root;
                public ICollection<long> Keys{get;}public ICollection<ArrivalAttempt> Values{get;}
                internal AttemptOrder(NavigationLiveArrivalTables root){this.root=root;Keys=new OrderedValues<long>(root,false);Values=new OrderedValues<ArrivalAttempt>(root,true);}
                public int Count=>root.Count;public bool IsReadOnly=>true;
                public ArrivalAttempt this[long key]{get=>root.Attempts[key];set=>throw new NotSupportedException();}
                public bool ContainsKey(long key)=>root.Attempts.ContainsKey(key);public bool TryGetValue(long key,out ArrivalAttempt value)=>root.Attempts.TryGetValue(key,out value);
                public IEnumerator<KeyValuePair<long,ArrivalAttempt>> GetEnumerator(){for(int i=0;i<root.Count;i++){long id=root.sortedIds[i];yield return new KeyValuePair<long,ArrivalAttempt>(id,root.Attempts[id]);}}
                IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
                public bool Contains(KeyValuePair<long,ArrivalAttempt> value)=>TryGetValue(value.Key,out var item)&&ReferenceEquals(item,value.Value);
                public void CopyTo(KeyValuePair<long,ArrivalAttempt>[] array,int offset){foreach(var value in this)array[offset++]=value;}
                public void Add(long key,ArrivalAttempt value)=>throw new NotSupportedException();public bool Remove(long key)=>throw new NotSupportedException();public void Clear()=>throw new NotSupportedException();public void Add(KeyValuePair<long,ArrivalAttempt> value)=>throw new NotSupportedException();public bool Remove(KeyValuePair<long,ArrivalAttempt> value)=>throw new NotSupportedException();
            }
            internal void ReleaseStorage(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>ReleaseStorage();
            protected override void ReleaseEdges(){Attempts?.Release();References?.Release();Ids?.Release();EmptyReferences?.Release();EmptyIds?.Release();ResetReferences?.Release();ResetIds?.Release();Attempts=null;References=EmptyReferences=ResetReferences=null;Ids=EmptyIds=ResetIds=null;}
        }
        private void InitializeLiveArrivalTables()
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Arrival,Generation,0,out var scope))throw new InvalidOperationException("Initial arrival tables admission refused.");
            using(scope){var root=NavigationLiveArrivalTables.AllocatePrepared(0,0,scope.OwnedReservation());try{root.SealPrepared();scope.Attach(root);scope.Commit(root,_=>{liveArrivalTables=root;arrivalAttempts=root.Ordered;arrivalProviders=root.References;arrivalProviderIds=root.Ids;});}catch{root.ReleaseStorage();throw;}}
        }
        private void UseLegacyArrivalTablesForDecode()
        {
            ResetLegacyArrivalLeaseTablesForDecode();var root=liveArrivalTables;liveArrivalTables=null;arrivalAttempts=new SortedDictionary<long,ArrivalAttempt>();arrivalProviders=new Dictionary<ILayeredNavigationProvider,ArrivalProviderRecord>(ProviderIdentityComparer.Instance);arrivalProviderIds=new Dictionary<long,ArrivalProviderRecord>();root?.Release();
        }
        private static int ArrivalProviderCapacity<TKey>(IDictionary<TKey,ArrivalProviderRecord> map)=>map is NavigationCommandSlots<TKey,ArrivalProviderRecord> slots?slots.Capacity:((Dictionary<TKey,ArrivalProviderRecord>)map).EnsureCapacity(0);
        private static int EnsureArrivalProviderCapacity<TKey>(IDictionary<TKey,ArrivalProviderRecord> map,int capacity)=>((Dictionary<TKey,ArrivalProviderRecord>)map).EnsureCapacity(capacity);
        private NavigationLiveArrivalProvider retiredArrivalProviders;
        private NavigationLiveArrivalAttempt retiredArrivalAttempts;
        private NavigationLiveArrivalProvider DetachRetiredArrivalProvidersNoThrow(){var result=retiredArrivalProviders;retiredArrivalProviders=null;return result;}
        private NavigationLiveArrivalAttempt DetachRetiredArrivalAttemptsNoThrow(){var result=retiredArrivalAttempts;retiredArrivalAttempts=null;return result;}
        private void RetireArrivalProviderNoThrow(NavigationLiveArrivalProvider owner){owner.RetiredNext=retiredArrivalProviders;retiredArrivalProviders=owner;}
        private void RetireArrivalAttemptNoThrow(NavigationLiveArrivalAttempt owner){owner.RetiredNext=retiredArrivalAttempts;retiredArrivalAttempts=owner;}
        internal static void ReleaseRetiredArrivalProviders(NavigationLiveArrivalProvider owner)
        {
            while(owner!=null){var next=owner.RetiredNext;owner.RetiredNext=null;var references=owner.RetiredReferenceMap;var ids=owner.RetiredIdMap;owner.RetiredReferenceMap=null;owner.RetiredIdMap=null;int count=owner.RetiredIndexReferences;owner.RetiredIndexReferences=0;references?.Release();ids?.Release();for(int i=0;i<count;i++)owner.Release();owner=next;}
        }
        private static void ReleaseRetiredArrivalAttempts(NavigationLiveArrivalAttempt owner)
        {while(owner!=null){var next=owner.RetiredNext;owner.RetiredNext=null;owner.Release();owner=next;}}
    }
}

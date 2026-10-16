using System;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal sealed class NavigationLiveArrivalLeaseTable : NavOwnedStorage
        {
            private static readonly long Header=NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveArrivalLeaseTable))+NavStorageLayout.ObjectBytesBound(typeof(LeaseOrder))+NavStorageLayout.ObjectBytesBound(typeof(LeaseValues<long>))+NavStorageLayout.ObjectBytesBound(typeof(LeaseValues<ArrivalProviderLease>));
            private static long Layout(int capacity)=>checked(Header+32L+8L*capacity);
            internal NavigationCommandSlots<long,ArrivalProviderLease> Tokens,EmptyTokens,ResetTokens;
            internal NavigationLiveArrivalLease RetirementHead,RetirementTail;
            internal NavigationLiveArrivalLeaseTable ResetTable;
            internal readonly LeaseOrder Ordered;
            private readonly long[] acquisitionTokens;
            internal int Revision;
            private bool released,sealedRoot;
            internal int Count=>Tokens.Count;
            internal long AcquisitionToken(int index)=>acquisitionTokens[index];
            internal override bool IsImmutableCaptureRoot=>false;
            private NavigationLiveArrivalLeaseTable(int capacity):base(Layout(capacity)){acquisitionTokens=new long[capacity];Ordered=new LeaseOrder(this);Retain();}
            // Match the existing Dictionary growth result, including ExpandPrime
            // when a full backing grows. This dictionary is admitted temporary
            // preparation only; the live backing is the concrete controlled map.
            internal static int GrowthCapacity(int current,int count,INavStorageReservation r)
            {
                if(current<0||current>65536||count<0||count>current)throw new ArgumentException("Invalid physical lease capacity.");if(count<current)return current;
                using(NavStorageScratch.Reserve(r,checked(4096L+512L*(current+1)))){
                    var probe=new Dictionary<long,byte>(current);if(probe.EnsureCapacity(0)!=current)throw new ArgumentException("Lease capacity is not the existing rounded capacity.");
                    for(int i=0;i<=current;i++)probe.Add(i,0);int capacity=probe.EnsureCapacity(0);if(capacity>65536)throw new InvalidOperationException("Physical lease growth exceeds wire capacity.");return capacity;
                }
            }
            internal static NavigationLiveArrivalLeaseTable AllocatePrepared(int capacity,INavStorageReservation r,NavigationLiveArrivalLeaseTable source=null)
            {
                if(capacity<0||capacity>65536)throw new ArgumentException("Invalid physical lease table capacity.");if(source!=null)NavStorageAdmission.RequireOwned(r,source);
                using(var allocation=new NavStorageAllocation(r.TryReserve)){
                    allocation.Reserve(Layout(capacity));var root=allocation.Register(new NavigationLiveArrivalLeaseTable(capacity));
                    try{
                        root.EmptyTokens=source==null?NavigationCommandSlots<long,ArrivalProviderLease>.CreateEmptyOwned(r):NavPhysicalMacro.Acquire(root,source.EmptyTokens);root.EmptyTokens.Seal();
                        if(capacity==0)root.Tokens=NavPhysicalMacro.Acquire(root,root.EmptyTokens);
                        else{root.Tokens=NavigationCommandSlots<long,ArrivalProviderLease>.CreateOwned(capacity,r);root.ResetTokens=NavPhysicalMacro.Acquire(root,root.EmptyTokens);}
                        if(capacity!=0)root.ResetTable=AllocatePrepared(0,r);
                        return root;
                    }catch{root.ReleaseStorage();throw;}
                }
            }
            internal void CopySourceObserved(NavigationLiveArrivalLeaseTable source)
            {
                if(sealedRoot||Count!=0||source==null||Tokens.Capacity<source.Tokens.Capacity)throw new ArgumentException("Lease source shape changed.");
                if(Tokens.Capacity!=0)Tokens.CopyPreparedFrom(source.Tokens);for(int i=0;i<source.Count;i++)acquisitionTokens[i]=source.acquisitionTokens[i];RetirementHead=source.RetirementHead;RetirementTail=source.RetirementTail;Revision=source.Revision;
            }
            internal void AddPreparing(NavigationLiveArrivalLease owner)
            {
                if(sealedRoot||owner==null||owner.Record.Retiring||Count>0&&Tokens[acquisitionTokens[Count-1]].Acquisition>=owner.Record.Acquisition)throw new ArgumentException("Lease acquisition order is not monotonic.");
                int at=Count;Tokens.AddPreparing(owner.Record.Token,owner.Record,owner);acquisitionTokens[at]=owner.Record.Token;Revision++;
            }
            internal void InitializeCatalogLayout(NavigationArrivalLeaseCatalog catalog)
            {
                if(sealedRoot||Count!=0||catalog.Capacity!=Tokens.Capacity)throw new ArgumentException("Invalid cold physical lease table shape.");catalog.InitializeLayout(Tokens);for(int i=0;i<catalog.Count;i++)acquisitionTokens[i]=catalog.AcquisitionToken(i);
            }
            internal void RebuildRetirementLinksPreparing(INavStorageReservation r)
            {
                if(sealedRoot||RetirementHead!=null||RetirementTail!=null)throw new ArgumentException("Physical lease retirement links already initialized.");
                using(NavStorageScratch.Reserve(r,checked(512L+8L*Count))){
                    var ordered=new NavigationLiveArrivalLease[Count];int count=0;for(int i=0;i<Count;i++){var owner=Tokens[acquisitionTokens[i]].StorageOwner;if(owner.Record.Retiring)ordered[count++]=owner;}
                    Array.Sort(ordered,0,count,RetirementOrder.Instance);
                    for(int i=0;i<count;i++){var owner=ordered[i];owner.RetirementPrevious=i==0?null:ordered[i-1];owner.RetirementNext=i+1==count?null:ordered[i+1];}RetirementHead=count==0?null:ordered[0];RetirementTail=count==0?null:ordered[count-1];
                }
            }
            private sealed class RetirementOrder : IComparer<NavigationLiveArrivalLease>
            {internal static readonly RetirementOrder Instance=new RetirementOrder();public int Compare(NavigationLiveArrivalLease a,NavigationLiveArrivalLease b)=>a.Record.Retirement.CompareTo(b.Record.Retirement);}
            internal void SealPrepared(){ResetTable?.SealPrepared();Tokens.Seal();sealedRoot=true;}
            internal NavigationLiveArrivalLeaseTable TakeEmptyResetNoThrow(){var value=ResetTable;ResetTable=null;return value;}
            internal void ClearRetirementLinksNoThrow(){for(int i=0;i<Count;i++){var owner=Tokens[acquisitionTokens[i]].StorageOwner;owner.RetirementPrevious=owner.RetirementNext=null;}RetirementHead=RetirementTail=null;}
            internal bool CanMarkRetiring(ArrivalProviderLease lease)
                =>lease?.StorageOwner!=null&&!lease.Retiring&&Tokens.FindOwnedSlot(lease.Token,lease,lease.StorageOwner)>=0&&lease.StorageOwner.RetirementPrevious==null&&lease.StorageOwner.RetirementNext==null;
            internal void MarkRetiringNoThrow(ArrivalProviderLease lease,long sequence)
            {
                var owner=lease.StorageOwner;owner.MarkRetiringNoThrow(sequence);owner.RetirementPrevious=RetirementTail;owner.RetirementNext=null;if(RetirementTail==null)RetirementHead=owner;else RetirementTail.RetirementNext=owner;RetirementTail=owner;Revision++;
            }
            internal bool CanExtract(ArrivalProviderLease lease,out int slot,out int acquisition)
            {
                var owner=lease?.StorageOwner;slot=owner==null?-1:Tokens.FindOwnedSlot(lease.Token,lease,owner);acquisition=-1;
                if(slot<0||owner.RetiredTokenMap!=null||Count==1&&(ResetTokens==null||ResetTable==null))return false;
                for(int i=0;i<Count;i++)if(acquisitionTokens[i]==lease.Token){acquisition=i;break;}
                if(acquisition<0)return false;
                if(!lease.Retiring)return owner.RetirementPrevious==null&&owner.RetirementNext==null;
                return (owner.RetirementPrevious==null?ReferenceEquals(RetirementHead,owner):ReferenceEquals(owner.RetirementPrevious.RetirementNext,owner))&&(owner.RetirementNext==null?ReferenceEquals(RetirementTail,owner):ReferenceEquals(owner.RetirementNext.RetirementPrevious,owner));
            }
            internal NavigationLiveArrivalLease ExtractNoThrow(int slot,int acquisition)
            {
                int count=Count;Tokens.ExtractPreparedNoThrow(slot,out var lease,out _);var owner=lease.StorageOwner;
                if(lease.Retiring){if(owner.RetirementPrevious==null)RetirementHead=owner.RetirementNext;else owner.RetirementPrevious.RetirementNext=owner.RetirementNext;if(owner.RetirementNext==null)RetirementTail=owner.RetirementPrevious;else owner.RetirementNext.RetirementPrevious=owner.RetirementPrevious;owner.RetirementPrevious=owner.RetirementNext=null;}
                for(int i=acquisition+1;i<count;i++)acquisitionTokens[i-1]=acquisitionTokens[i];acquisitionTokens[count-1]=0;
                if(Tokens.Count==0){owner.RetiredTokenMap=Tokens;Tokens=ResetTokens;ResetTokens=null;}owner.DetachNoThrow();Revision++;return owner;
            }
            internal sealed class LeaseValues<T> : ICollection<T>
            {
                private readonly NavigationLiveArrivalLeaseTable root;private readonly bool values;
                internal LeaseValues(NavigationLiveArrivalLeaseTable root,bool values){this.root=root;this.values=values;}
                public int Count=>root.Count;public bool IsReadOnly=>true;
                public IEnumerator<T> GetEnumerator(){for(int i=0;i<root.Count;i++){long token=root.acquisitionTokens[i];yield return values?(T)(object)root.Tokens[token]:(T)(object)token;}}
                IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();public bool Contains(T value){foreach(var item in this)if(EqualityComparer<T>.Default.Equals(value,item))return true;return false;}
                public void CopyTo(T[] array,int offset){foreach(var value in this)array[offset++]=value;}
                public void Add(T value)=>throw new NotSupportedException();public bool Remove(T value)=>throw new NotSupportedException();public void Clear()=>throw new NotSupportedException();
            }
            internal sealed class LeaseOrder : IDictionary<long,ArrivalProviderLease>
            {
                private readonly NavigationLiveArrivalLeaseTable root;
                public ICollection<long> Keys{get;}public ICollection<ArrivalProviderLease> Values{get;}
                internal LeaseOrder(NavigationLiveArrivalLeaseTable root){this.root=root;Keys=new LeaseValues<long>(root,false);Values=new LeaseValues<ArrivalProviderLease>(root,true);}
                public int Count=>root.Count;public bool IsReadOnly=>true;
                public ArrivalProviderLease this[long token]{get=>root.Tokens[token];set=>throw new NotSupportedException();}
                public bool ContainsKey(long token)=>root.Tokens.ContainsKey(token);public bool TryGetValue(long token,out ArrivalProviderLease value)=>root.Tokens.TryGetValue(token,out value);
                public IEnumerator<KeyValuePair<long,ArrivalProviderLease>> GetEnumerator(){for(int i=0;i<root.Count;i++){long token=root.acquisitionTokens[i];yield return new KeyValuePair<long,ArrivalProviderLease>(token,root.Tokens[token]);}}
                IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();public bool Contains(KeyValuePair<long,ArrivalProviderLease> value)=>TryGetValue(value.Key,out var row)&&ReferenceEquals(row,value.Value);
                public void CopyTo(KeyValuePair<long,ArrivalProviderLease>[] array,int offset){foreach(var value in this)array[offset++]=value;}
                public void Add(long token,ArrivalProviderLease value)=>throw new NotSupportedException();public bool Remove(long token)=>throw new NotSupportedException();public void Clear()=>throw new NotSupportedException();public void Add(KeyValuePair<long,ArrivalProviderLease> value)=>throw new NotSupportedException();public bool Remove(KeyValuePair<long,ArrivalProviderLease> value)=>throw new NotSupportedException();
            }
            internal void ReleaseStorage(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>ReleaseStorage();
            protected override void ReleaseEdges(){ResetTable?.Release();ResetTable=null;Tokens?.Release();EmptyTokens?.Release();ResetTokens?.Release();Tokens=EmptyTokens=ResetTokens=null;RetirementHead=RetirementTail=null;}
        }
    }
}

using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed class NavigationArrivalLeaseCatalog : NavOwnedStorage,INavPhysicalBody
    {
        internal readonly struct Values
        {
            internal readonly long World,Sequence,Acquisitions,Retirements,Releases;
            internal Values(long world,long sequence,long acquisitions,long retirements,long releases){World=world;Sequence=sequence;Acquisitions=acquisitions;Retirements=retirements;Releases=releases;}
            internal void Validate(){if(World<1||Sequence<0||Acquisitions!=Sequence||Retirements<0||Releases<0||Releases>Sequence)throw new ArgumentException("Invalid physical lease catalog sequences.");}
        }
        private static readonly long Header=NavStorageLayout.ObjectBytesBound(typeof(NavigationArrivalLeaseCatalog));
        private static long Layout(int count,int layout)=>checked(Header+96L+16L*count+((4L*layout+7)/8*8));
        internal Values Data{get;private set;}
        internal readonly int Capacity;
        private readonly NavigationArrivalLeaseSnapshot[] leases;
        private readonly int[] slots;
        private readonly long[] acquisitionTokens;
        private bool observed,sealedRoot,released;
        internal int Count=>leases.Length;internal int LayoutLength=>slots.Length;
        internal NavigationArrivalLeaseSnapshot Lease(int index)=>leases[index];internal int LayoutValue(int index)=>slots[index];internal long AcquisitionToken(int index)=>acquisitionTokens[index];
        internal override bool IsImmutableCaptureRoot=>sealedRoot;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ArrivalLeaseCatalog);
        private NavigationArrivalLeaseCatalog(int capacity,int count,int layout,bool owner):base(Layout(count,layout)){Capacity=capacity;leases=new NavigationArrivalLeaseSnapshot[count];slots=new int[layout];acquisitionTokens=new long[count];if(owner)Retain();}
        internal static NavigationArrivalLeaseCatalog AllocatePrepared(int capacity,int count,int layout,INavStorageReservation r,bool owner=true)
        {
            if(capacity<0||capacity>65536||count<0||count>capacity||layout<1||layout>capacity+1)throw new ArgumentException("Invalid physical lease catalog shape.");
            using(var allocation=new NavStorageAllocation(r.TryReserve)){allocation.Reserve(Layout(count,layout));return allocation.Register(new NavigationArrivalLeaseCatalog(capacity,count,layout,owner));}
        }
        internal void ObserveValues(Values data){if(observed||sealedRoot)throw new InvalidOperationException("Physical lease catalog already observed.");data.Validate();Data=data;observed=true;}
        internal void ObserveLease(int index,NavigationArrivalLeaseSnapshot value){if(sealedRoot||leases[index]!=null)throw new InvalidOperationException("Physical lease role already assigned.");leases[index]=NavPhysicalMacro.Acquire(this,value);}
        internal void ObserveLayout(NavigationCommandSlots<long,ArrivalProviderLease> source){if(sealedRoot)throw new InvalidOperationException("Frozen lease layout.");source.CopyLayoutTo(slots);}
        internal void ObserveAcquisition(int index,long token){if(sealedRoot)throw new InvalidOperationException("Frozen acquisition order.");acquisitionTokens[index]=token;}
        internal void InitializeLayout(NavigationCommandSlots<long,ArrivalProviderLease> target){if(!sealedRoot)throw new InvalidOperationException("Unsealed physical lease catalog.");if(target.Capacity!=0)target.InitializeLayoutPreparing(slots);}
        private sealed class TokenOrder : IComparer<NavigationArrivalLeaseSnapshot>
        {internal static readonly TokenOrder Instance=new TokenOrder();public int Compare(NavigationArrivalLeaseSnapshot a,NavigationArrivalLeaseSnapshot b)=>a.Data.Token.CompareTo(b.Data.Token);}
        private static int Find(NavigationArrivalLeaseSnapshot[] rows,long token)
        {int low=0,high=rows.Length-1;while(low<=high){int mid=low+(high-low)/2;long key=rows[mid].Data.Token;if(key==token)return mid;if(key<token)low=mid+1;else high=mid-1;}return -1;}
        internal void SealPrepared(INavStorageReservation r)
        {
            if(!observed||sealedRoot||Data.Sequence-Data.Releases!=Count)throw new ArgumentException("Physical lease count differs from sequences.");Data.Validate();
            using(NavStorageScratch.Reserve(r,checked(8192L+256L*Count+64L*Capacity))){
                var rounded=new Dictionary<long,byte>(Capacity);if(rounded.EnsureCapacity(0)!=Capacity)throw new ArgumentException("Physical lease capacity differs from existing rounded contract.");
                int length=slots[0];if(length<0||length>Capacity||slots.Length-1>length||length-(slots.Length-1)!=Count||Count==0&&Capacity!=0)throw new ArgumentException("Physical lease slot/count/reset mismatch.");
                var holes=new bool[length];for(int i=1;i<slots.Length;i++){int slot=slots[i];if(slot<0||slot>=length||holes[slot])throw new ArgumentException("Invalid physical lease free-slot order.");holes[slot]=true;}
                var sorted=new NavigationArrivalLeaseSnapshot[Count];var used=new bool[Count];var requests=new HashSet<long>();var retirements=new HashSet<long>();
                for(int i=0;i<Count;i++){var value=leases[i];if(value==null||!value.IsImmutableCaptureRoot||value.Data.World!=Data.World||value.Data.Token>Data.Sequence||value.Data.Acquisition>Data.Acquisitions||value.Data.Retirement>Data.Retirements||!requests.Add(value.Data.Request)||value.Data.Retiring&&!retirements.Add(value.Data.Retirement))throw new ArgumentException("Invalid physical lease catalog row.");sorted[i]=value;}
                Array.Sort(sorted,TokenOrder.Instance);for(int i=1;i<Count;i++)if(sorted[i-1].Data.Token>=sorted[i].Data.Token)throw new ArgumentException("Duplicate physical lease token.");
                long acquisition=0;for(int i=0;i<Count;i++){int index=Find(sorted,acquisitionTokens[i]);if(index<0||used[index]||sorted[index].Data.Acquisition<=acquisition)throw new ArgumentException("Physical lease acquisition order differs.");used[index]=true;acquisition=sorted[index].Data.Acquisition;}
            }
            sealedRoot=true;
        }
        internal void ValidateArrivalCatalog(NavigationArrivalCatalog arrival,INavStorageReservation r)
        {
            if(!sealedRoot||arrival==null||!arrival.IsImmutableCaptureRoot||Data.World!=arrival.Data.World||Data.Sequence!=arrival.Data.Leases||Data.Acquisitions!=arrival.Data.Acquisitions||Data.Retirements!=arrival.Data.Retirements||Data.Releases!=arrival.Data.Releases||Count!=arrival.LeaseCount)throw new ArgumentException("Physical lease/arrival catalog sequences differ.");
            using(NavStorageScratch.Reserve(r,checked(8192L+256L*(Count+arrival.AttemptCount+arrival.ProviderCount)))){
                var providers=new Dictionary<long,NavigationArrivalProviderSnapshot>();var attempts=new Dictionary<long,NavigationArrivalAttemptSnapshot>();var retirements=new HashSet<long>();var tokens=new Dictionary<long,long>();
                for(int i=0;i<arrival.ProviderCount;i++)providers.Add(arrival.ProviderId(i).Id,arrival.ProviderId(i));
                for(int i=0;i<arrival.AttemptCount;i++){var value=arrival.Attempt(i);attempts.Add(value.Data.Id,value);if(value.Data.Retiring&&!retirements.Add(value.Data.Retirement))throw new ArgumentException("Duplicate attempt retirement sequence.");}
                for(int i=0;i<Count;i++){
                    var value=leases[i];var d=value.Data;if(!providers.TryGetValue(d.Provider,out var provider)||!ReferenceEquals(provider.ProviderRoot,value.ProviderRoot)||!attempts.TryGetValue(d.Attempt,out var attempt)||!attempt.Data.Published||attempt.Data.Status!=NavArrivalStatus.Ready||d.Retiring&&!retirements.Add(d.Retirement))throw new ArgumentException("Physical lease exact provider/attempt role differs.");
                    bool found=false;foreach(var request in attempt.Intents.Requests)if(request.Request==d.Request&&request.Entity==d.Entity&&request.MemberIdentity.Incarnation==d.Incarnation&&request.Order==d.Order){found=true;break;}if(!found)throw new ArgumentException("Physical lease member identity differs from attempt.");tokens.Add(d.Token,d.Provider);
                }
                for(int i=0;i<Count;i++)if(!tokens.TryGetValue(arrival.LeaseToken(i),out long provider)||provider!=arrival.LeaseProvider(i))throw new ArgumentException("Physical lease token/provider role differs from arrival catalog.");
            }
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!sealedRoot||StorageBytes!=NavStorageLayout.Charge(Layout(Count,slots.Length)))throw new ArgumentException("Invalid physical lease catalog ownership.");var w=writer.Output;w.Write(Data.World);w.Write(Data.Sequence);w.Write(Data.Acquisitions);w.Write(Data.Retirements);w.Write(Data.Releases);w.Write(Capacity);w.Write(Count);w.Write(slots.Length);foreach(int value in slots)w.Write(value);foreach(long value in acquisitionTokens)w.Write(value);foreach(var value in leases)NavPhysicalMacro.WriteEdge(writer,this,value);
        }
        internal static NavigationArrivalLeaseCatalog ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var r=reader.Input;var data=new Values(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64());data.Validate();int capacity=r.ReadInt32(),count=r.ReadInt32(),length=r.ReadInt32();var root=AllocatePrepared(capacity,count,length,reader.Reservation,false);root.ObserveValues(data);for(int i=0;i<length;i++)root.slots[i]=r.ReadInt32();for(int i=0;i<count;i++)root.acquisitionTokens[i]=r.ReadInt64();for(int i=0;i<count;i++)root.ObserveLease(i,NavPhysicalMacro.ReadEdge<NavigationArrivalLeaseSnapshot>(reader));root.SealPrepared(reader.Reservation);return root;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){foreach(var value in leases)value?.Release();}
    }
}

using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed class NavigationArrivalCatalog : NavOwnedStorage,INavPhysicalBody
    {
        internal readonly struct Values
        {
            internal readonly long World,Requests,Attempts,Commits,Providers,Leases,Acquisitions,Retirements,Releases;
            internal Values(long world,long requests,long attempts,long commits,long providers,long leases,long acquisitions,long retirements,long releases)
            {World=world;Requests=requests;Attempts=attempts;Commits=commits;Providers=providers;Leases=leases;Acquisitions=acquisitions;Retirements=retirements;Releases=releases;}
            internal void Validate(){if(World<1||Requests<0||Attempts<0||Commits<0||Providers<0||Leases<0||Acquisitions!=Leases||Retirements<0||Releases<0||Releases>Leases)throw new ArgumentException("Invalid arrival catalog sequences.");}
        }
        private static readonly long Header=NavStorageLayout.ObjectBytesBound(typeof(NavigationArrivalCatalog));
        private static long Align(long value)=>checked((value+7)/8*8);
        private static long Layout(int attempts,int providers,int leases,int attemptLayout,int referenceLayout,int idLayout)=>checked(Header+9*32L+16L*attempts+16L*providers+16L*leases+Align(4L*attemptLayout)+Align(4L*referenceLayout)+Align(4L*idLayout));
        internal Values Data{get;private set;}
        internal readonly int AttemptCapacity,ReferenceCapacity,IdCapacity;
        private readonly NavigationArrivalAttemptSnapshot[] attempts;
        private readonly NavigationArrivalProviderSnapshot[] references,ids;
        private readonly int[] attemptLayout,referenceLayout,idLayout;
        private readonly long[] sortedAttempts,leaseTokens,leaseProviders;
        private bool observed,sealedRoot,released;
        internal int AttemptCount=>attempts.Length;internal int ProviderCount=>ids.Length;internal int LeaseCount=>leaseTokens.Length;
        internal NavigationArrivalAttemptSnapshot Attempt(int index)=>attempts[index];
        internal NavigationArrivalProviderSnapshot Reference(int index)=>references[index];internal NavigationArrivalProviderSnapshot ProviderId(int index)=>ids[index];
        internal long SortedAttempt(int index)=>sortedAttempts[index];internal long LeaseToken(int index)=>leaseTokens[index];internal long LeaseProvider(int index)=>leaseProviders[index];
        internal int LayoutLength(int role)=>LayoutFor(role).Length;internal int LayoutValue(int role,int index)=>LayoutFor(role)[index];
        private int[] LayoutFor(int role)=>role==0?attemptLayout:role==1?referenceLayout:role==2?idLayout:throw new ArgumentOutOfRangeException(nameof(role));
        internal override bool IsImmutableCaptureRoot=>sealedRoot;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ArrivalCatalog);
        private NavigationArrivalCatalog(int ac,int rc,int ic,int a,int p,int l,int al,int rl,int il,bool owner):base(Layout(a,p,l,al,rl,il))
        {AttemptCapacity=ac;ReferenceCapacity=rc;IdCapacity=ic;attempts=new NavigationArrivalAttemptSnapshot[a];references=new NavigationArrivalProviderSnapshot[p];ids=new NavigationArrivalProviderSnapshot[p];attemptLayout=new int[al];referenceLayout=new int[rl];idLayout=new int[il];sortedAttempts=new long[a];leaseTokens=new long[l];leaseProviders=new long[l];if(owner)Retain();}
        internal static NavigationArrivalCatalog AllocatePrepared(int ac,int rc,int ic,int a,int p,int l,int al,int rl,int il,INavStorageReservation reservation,bool owner=true)
        {
            if(ac<0||ac>AuthoritySlotMap<int,int>.MaximumSlots||rc<0||rc>65536||ic<0||ic>65536||a<0||a>ac||p<0||p>rc||p>ic||l<0||l>65536||al<1||al>ac+1||rl<1||rl>rc+1||il<1||il>ic+1)throw new ArgumentException("Invalid arrival catalog backing shape.");
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout(a,p,l,al,rl,il));return allocation.Register(new NavigationArrivalCatalog(ac,rc,ic,a,p,l,al,rl,il,owner));}
        }
        internal void ObserveValues(Values values){if(observed||sealedRoot)throw new InvalidOperationException("Arrival catalog already observed.");values.Validate();Data=values;observed=true;}
        internal void ObserveAttempt(int index,NavigationArrivalAttemptSnapshot value){if(sealedRoot||attempts[index]!=null)throw new InvalidOperationException("Arrival attempt role already filled.");attempts[index]=NavPhysicalMacro.Acquire(this,value);}
        internal void ObserveReference(int index,NavigationArrivalProviderSnapshot value){if(sealedRoot||references[index]!=null)throw new InvalidOperationException("Arrival reference role already filled.");references[index]=NavPhysicalMacro.Acquire(this,value);}
        internal void ObserveProviderId(int index,NavigationArrivalProviderSnapshot value){if(sealedRoot||ids[index]!=null)throw new InvalidOperationException("Arrival provider-ID role already filled.");ids[index]=NavPhysicalMacro.Acquire(this,value);}
        internal void ObserveSortedAttempt(int index,long value){if(sealedRoot)throw new InvalidOperationException("Frozen arrival catalog.");sortedAttempts[index]=value;}
        internal void ObserveLease(int index,long token,long provider){if(sealedRoot)throw new InvalidOperationException("Frozen arrival catalog.");leaseTokens[index]=token;leaseProviders[index]=provider;}
        internal void ObserveLayout<TKey,TValue>(int role,NavigationCommandSlots<TKey,TValue> source){if(sealedRoot)throw new InvalidOperationException("Frozen arrival catalog.");source.CopyLayoutTo(LayoutFor(role));}
        internal void InitializeLayout<TKey,TValue>(int role,NavigationCommandSlots<TKey,TValue> destination){if(!sealedRoot)throw new InvalidOperationException("Unsealed arrival catalog.");destination.InitializeLayoutPreparing(LayoutFor(role));}
        internal void PrepareLeaseOrder(){if(sealedRoot)throw new InvalidOperationException("Frozen arrival catalog.");Array.Sort(leaseTokens,leaseProviders);}
        private sealed class ProviderOrder : IComparer<NavigationArrivalProviderSnapshot>
        {internal static readonly ProviderOrder Instance=new ProviderOrder();public int Compare(NavigationArrivalProviderSnapshot a,NavigationArrivalProviderSnapshot b)=>a.Id.CompareTo(b.Id);}
        private sealed class ProviderIdentity : IEqualityComparer<NavDeclarationStorage>
        {internal static readonly ProviderIdentity Instance=new ProviderIdentity();public bool Equals(NavDeclarationStorage a,NavDeclarationStorage b)=>ReferenceEquals(a,b);public int GetHashCode(NavDeclarationStorage value)=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);}
        private static int FindProvider(NavigationArrivalProviderSnapshot[] sorted,long id)
        {int low=0,high=sorted.Length-1;while(low<=high){int middle=low+(high-low)/2;long key=sorted[middle].Id;if(key==id)return middle;if(key<id)low=middle+1;else high=middle-1;}return -1;}
        private static void ValidateLayout(int[] layout,int capacity,int count)
        {
            int length=layout[0];if(length<0||length>capacity||layout.Length-1>length||length-(layout.Length-1)!=count)throw new ArgumentException("Arrival slot layout/count mismatch.");
            var holes=new bool[length];for(int i=1;i<layout.Length;i++){int slot=layout[i];if(slot<0||slot>=length||holes[slot])throw new ArgumentException("Invalid arrival free-slot order.");holes[slot]=true;}
        }
        internal void SealPrepared(INavStorageReservation reservation)
        {
            if(!observed||sealedRoot||Data.Leases-Data.Releases!=LeaseCount)throw new ArgumentException("Arrival catalog observation/lease count mismatch.");Data.Validate();
            using(NavStorageScratch.Reserve(reservation,checked(8192L+256L*ProviderCount+64L*AttemptCount+64L*LeaseCount+AttemptCapacity+ReferenceCapacity+IdCapacity))){
                ValidateLayout(attemptLayout,AttemptCapacity,AttemptCount);ValidateLayout(referenceLayout,ReferenceCapacity,ProviderCount);ValidateLayout(idLayout,IdCapacity,ProviderCount);
                var ordered=new NavigationArrivalProviderSnapshot[ProviderCount];var counts=new int[ProviderCount];var used=new bool[ProviderCount];var seenAttempts=new bool[AttemptCount];var roots=new HashSet<NavDeclarationStorage>(ProviderCount,ProviderIdentity.Instance);
                for(int i=0;i<ProviderCount;i++){var value=ids[i];if(value==null||!value.IsImmutableCaptureRoot||value.Id>Data.Providers||!roots.Add(value.ProviderRoot))throw new ArgumentException("Invalid or aliased provider-ID row.");ordered[i]=value;}
                Array.Sort(ordered,ProviderOrder.Instance);for(int i=1;i<ordered.Length;i++)if(ordered[i-1].Id>=ordered[i].Id)throw new ArgumentException("Duplicate provider identity.");
                for(int i=0;i<ProviderCount;i++){var value=references[i];int index=value==null?-1:FindProvider(ordered,value.Id);if(index<0||used[index]||!ReferenceEquals(value,ordered[index]))throw new ArgumentException("Provider reference/index record alias mismatch.");used[index]=true;}
                for(int i=0;i<AttemptCount;i++)if(sortedAttempts[i]<1||sortedAttempts[i]>Data.Attempts||i>0&&sortedAttempts[i-1]>=sortedAttempts[i])throw new ArgumentException("Invalid sorted attempt IDs.");
                for(int i=0;i<AttemptCount;i++){
                    var value=attempts[i];if(value==null||!value.IsImmutableCaptureRoot||value.Data.Retirement>Data.Retirements)throw new ArgumentException("Invalid attempt slot role.");int at=Array.BinarySearch(sortedAttempts,value.Data.Id);if(at<0||seenAttempts[at])throw new ArgumentException("Attempt slot/sorted index differs.");seenAttempts[at]=true;
                    foreach(var request in value.Intents.Requests)if(request.Session!=Data.World||request.Request<1||request.Request>Data.Requests||request.MemberIdentity.GroupId!=value.Data.Group)throw new ArgumentException("Attempt request identity differs from catalog.");
                    int input=FindProvider(ordered,value.Data.InputProviderId);if(input<0||!ReferenceEquals(ordered[input].ProviderRoot,value.ProviderRoot(0)))throw new ArgumentException("Input provider ID/owner mismatch.");
                    for(int n=0;n<value.WorkingCount;n++){int p=FindProvider(ordered,value.WorkingId(n));if(p<0||!ReferenceEquals(ordered[p].ProviderRoot,(value.Work.PhysicalInput.WorkingBindings[n].Provider as INavStorageFacade)?.StorageRoot))throw new ArgumentException("Working provider ID/owner mismatch.");}
                    for(int n=0;n<value.ProviderCount;n++){
                        var root=value.ProviderRoot(n);long provider=value.Data.InputProviderId;
                        if(n!=0){provider=0;for(int b=0;b<value.WorkingCount;b++)if(ReferenceEquals(root,(value.Work.PhysicalInput.WorkingBindings[b].Provider as INavStorageFacade)?.StorageRoot)){provider=value.WorkingId(b);break;}}
                        int p=FindProvider(ordered,provider);if(p<0||!ReferenceEquals(root,ordered[p].ProviderRoot))throw new ArgumentException("Unbound distinct provider owner.");counts[p]++;
                    }
                }
                for(int i=0;i<LeaseCount;i++){long token=leaseTokens[i];int p=FindProvider(ordered,leaseProviders[i]);if(token<1||token>Data.Leases||i>0&&leaseTokens[i-1]>=token||p<0)throw new ArgumentException("Invalid frozen lease provider role.");counts[p]++;}
                for(int i=0;i<ProviderCount;i++)if(counts[i]!=ordered[i].LogicalReferences)throw new ArgumentException("Provider logical references differ from owned attempts/leases.");
            }
            sealedRoot=true;
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!sealedRoot||StorageBytes!=NavStorageLayout.Charge(Layout(AttemptCount,ProviderCount,LeaseCount,attemptLayout.Length,referenceLayout.Length,idLayout.Length)))throw new ArgumentException("Invalid arrival catalog ownership.");
            var w=writer.Output;w.Write(Data.World);w.Write(Data.Requests);w.Write(Data.Attempts);w.Write(Data.Commits);w.Write(Data.Providers);w.Write(Data.Leases);w.Write(Data.Acquisitions);w.Write(Data.Retirements);w.Write(Data.Releases);
            w.Write(AttemptCapacity);w.Write(ReferenceCapacity);w.Write(IdCapacity);w.Write(AttemptCount);w.Write(ProviderCount);w.Write(LeaseCount);w.Write(attemptLayout.Length);w.Write(referenceLayout.Length);w.Write(idLayout.Length);
            foreach(int value in attemptLayout)w.Write(value);foreach(int value in referenceLayout)w.Write(value);foreach(int value in idLayout)w.Write(value);foreach(long value in sortedAttempts)w.Write(value);
            for(int i=0;i<LeaseCount;i++){w.Write(leaseTokens[i]);w.Write(leaseProviders[i]);}
            foreach(var value in attempts)NavPhysicalMacro.WriteEdge(writer,this,value);foreach(var value in references)NavPhysicalMacro.WriteEdge(writer,this,value);foreach(var value in ids)NavPhysicalMacro.WriteEdge(writer,this,value);
        }
        internal static NavigationArrivalCatalog ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var r=reader.Input;var data=new Values(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64());data.Validate();int ac=r.ReadInt32(),rc=r.ReadInt32(),ic=r.ReadInt32(),a=r.ReadInt32(),p=r.ReadInt32(),l=r.ReadInt32(),al=r.ReadInt32(),rl=r.ReadInt32(),il=r.ReadInt32();var root=AllocatePrepared(ac,rc,ic,a,p,l,al,rl,il,reader.Reservation,false);root.ObserveValues(data);
            for(int i=0;i<al;i++)root.attemptLayout[i]=r.ReadInt32();for(int i=0;i<rl;i++)root.referenceLayout[i]=r.ReadInt32();for(int i=0;i<il;i++)root.idLayout[i]=r.ReadInt32();for(int i=0;i<a;i++)root.sortedAttempts[i]=r.ReadInt64();for(int i=0;i<l;i++)root.ObserveLease(i,r.ReadInt64(),r.ReadInt64());
            for(int i=0;i<a;i++)root.ObserveAttempt(i,NavPhysicalMacro.ReadEdge<NavigationArrivalAttemptSnapshot>(reader));for(int i=0;i<p;i++)root.ObserveReference(i,NavPhysicalMacro.ReadEdge<NavigationArrivalProviderSnapshot>(reader));for(int i=0;i<p;i++)root.ObserveProviderId(i,NavPhysicalMacro.ReadEdge<NavigationArrivalProviderSnapshot>(reader));root.SealPrepared(reader.Reservation);return root;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){foreach(var value in attempts)value?.Release();foreach(var value in references)value?.Release();foreach(var value in ids)value?.Release();}
    }
}

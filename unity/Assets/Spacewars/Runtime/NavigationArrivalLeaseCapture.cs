using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private NavigationLiveArrivalLeaseTable liveArrivalLeaseTable;
        private int ArrivalLeaseCapacity=>liveArrivalLeaseTable?.Tokens.Capacity??((System.Collections.Generic.Dictionary<long,ArrivalProviderLease>)arrivalProjectionLeases).EnsureCapacity(0);
        private int EnsureArrivalLeaseCapacity(int capacity)=>((System.Collections.Generic.Dictionary<long,ArrivalProviderLease>)arrivalProjectionLeases).EnsureCapacity(capacity);
        private void ResetLegacyArrivalLeaseTablesForDecode()
        {
            var root=liveArrivalLeaseTable;liveArrivalLeaseTable=null;arrivalProjectionLeases=new System.Collections.Generic.Dictionary<long,ArrivalProviderLease>();arrivalRetirements=new System.Collections.Generic.SortedDictionary<long,ArrivalProviderLease>();root?.Release();
        }
        private void RestoreLiveArrivalLeaseCatalog(NavigationArrivalLeaseCatalog catalog,NavigationArrivalCatalog arrival,NavigationHostRoot host)
        {
            if(host==null||!physicalArrivalEnabled||catalog.Count!=arrivalProjectionLeases.Count)throw new ArgumentException("Physical lease BODY has no matching host/authority tail.");
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Physical lease decode admission refused.");
            using(scope){NavigationLiveArrivalLeaseTable table=null;try{
                var r=scope.OwnedReservation();catalog.ValidateArrivalCatalog(arrival,r);table=NavigationLiveArrivalLeaseTable.AllocatePrepared(catalog.Capacity,r);table.InitializeCatalogLayout(catalog);
                int index=0;for(int slot=0;slot<catalog.LayoutValue(0);slot++)if(!table.Tokens.IsPreparedHole(slot)){
                    var leaf=catalog.Lease(index++);var d=leaf.Data;
                    if(!arrivalProjectionLeases.TryGetValue(d.Token,out var original)||!NavigationArrivalLeaseValues.Read(original).Same(d)||!ReferenceEquals((original.Provider as INavStorageFacade)?.StorageRoot,leaf.ProviderRoot)||original.Binding!=leaf.Binding.Value||original.Reason!=leaf.CurrentReason||original.Projection!=null||original.PointsCapacity!=0||original.TypedCapacity!=0||original.ObservedCapacity!=0||original.StagingBytes!=0)throw new ArgumentException("Physical lease authority differs from exact BODY leaf.");
                    var owner=NavigationLiveArrivalLease.CreateOwned(d,this,(ILayeredNavigationProvider)leaf.ProviderRoot.Facade,r,leaf);try{table.Tokens.AddAtPreparedSlot(slot,d.Token,owner.Record,owner);}finally{owner.ReleaseStorage();}
                }
                table.RebuildRetirementLinksPreparing(r);table.SealPrepared();scope.Attach(table);scope.Commit(table,_=>{liveArrivalLeaseTable=table;arrivalProjectionLeases=table.Ordered;arrivalRetirements=null;});table=null;
            }finally{table?.ReleaseStorage();}}
        }
        private sealed class ArrivalLeaseCapture : IDisposable
        {
            internal readonly NavigationLiveArrivalLeaseTable Source;
            internal readonly int Revision;
            internal readonly NavigationArrivalLeaseCatalog Catalog;
            private readonly NavigationLiveArrivalLease[] owners;
            private IDisposable scratch;
            internal ArrivalLeaseCapture(NavigationLiveArrivalLeaseTable source,int revision,int capacity,int count,int layout,INavStorageReservation r)
            {
                Source=source;Revision=revision;scratch=NavStorageScratch.Reserve(r,checked(NavStorageLayout.ObjectBytesBound(typeof(ArrivalLeaseCapture))+32L+8L*count));
                try{owners=new NavigationLiveArrivalLease[count];Catalog=NavigationArrivalLeaseCatalog.AllocatePrepared(capacity,count,layout,r);}catch{scratch.Dispose();scratch=null;throw;}
            }
            internal void PinShapesObserved()
            {
                int n=0;for(int i=0;i<Source.Tokens.Capacity;i++)if(Source.Tokens.TryReadSlot(i,out _,out var value)){var owner=value.StorageOwner;if(owner==null||n==owners.Length)throw new InvalidOperationException("Physical lease capture shape changed.");owner.Retain();owners[n++]=owner;}if(n!=owners.Length)throw new InvalidOperationException("Physical lease capture count changed.");
            }
            internal void AllocateLeaves(INavStorageReservation r)
            {for(int i=0;i<owners.Length;i++){var leaf=NavigationArrivalLeaseSnapshot.AllocatePrepared(r);try{Catalog.ObserveLease(i,leaf);}finally{leaf.ReleaseStorage();}}}
            internal void Observe(NavigationSession session)
            {
                if(!ReferenceEquals(session.liveArrivalLeaseTable,Source)||Source.Revision!=Revision||session.arrivalProjectionLeases.Count!=Catalog.Count)throw new InvalidOperationException("Physical lease capture source changed.");Catalog.ObserveValues(new NavigationArrivalLeaseCatalog.Values(session.Generation,session.arrivalLeaseSequence,session.arrivalAcquisitionSequence,session.arrivalRetirementSequence,session.arrivalReleaseSequence));
                for(int i=0;i<owners.Length;i++){
                    var owner=owners[i];var lease=owner.Record;
                    if(!ReferenceEquals(lease.StorageOwner,owner)||!ReferenceEquals((lease.Provider as INavStorageFacade)?.StorageRoot,owner.ProviderRoot)||!ReferenceEquals(lease.Binding,owner.Binding.Value)||!ReferenceEquals(lease.Reason,lease.Retiring?owner.RemovedReason.Value:owner.InitialReason.Value)||lease.Projection!=null||lease.PointsCapacity!=0||lease.TypedCapacity!=0||lease.ObservedCapacity!=0||lease.StagingBytes!=0)throw new InvalidOperationException("Physical lease capture roles differ from owner.");
                    Catalog.Lease(i).ObservePrepared(NavigationArrivalLeaseValues.Read(lease),owner.ProviderRoot,owner.Binding,owner.InitialReason,owner.RemovedReason);Catalog.ObserveAcquisition(i,Source.AcquisitionToken(i));
                }
                Catalog.ObserveLayout(Source.Tokens);
            }
            internal void Seal(INavStorageReservation r){for(int i=0;i<Catalog.Count;i++)Catalog.Lease(i).SealPrepared(r);Catalog.SealPrepared(r);}
            public void Dispose(){Catalog?.ReleaseStorage();foreach(var owner in owners)owner?.Release();Source.Release();scratch?.Dispose();scratch=null;}
        }
        private ArrivalLeaseCapture PrepareArrivalLeaseCapture(INavStorageReservation r)
        {
            NavigationLiveArrivalLeaseTable source;int revision,capacity,count,layout;
            using(EnterTransport()){source=liveArrivalLeaseTable;if(source==null)return null;source.Retain();revision=source.Revision;capacity=source.Tokens.Capacity;count=source.Count;layout=source.Tokens.LayoutLength;}
            ArrivalLeaseCapture capture=null;
            try{capture=new ArrivalLeaseCapture(source,revision,capacity,count,layout,r);using(EnterTransport()){if(!ReferenceEquals(liveArrivalLeaseTable,source)||source.Revision!=revision)throw new InvalidOperationException("Physical lease capture source changed during preparation.");capture.PinShapesObserved();}capture.AllocateLeaves(r);return capture;}
            catch{if(capture!=null)capture.Dispose();else source.Release();throw;}
        }
    }
}

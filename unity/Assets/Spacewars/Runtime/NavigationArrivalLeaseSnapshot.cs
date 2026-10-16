using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed class NavigationArrivalLeaseSnapshot : NavOwnedStorage,INavPhysicalBody
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationArrivalLeaseSnapshot));
        internal NavigationArrivalLeaseValues Data{get;private set;}
        internal NavDeclarationStorage ProviderRoot;
        internal NavOwnedText Binding,InitialReason,RemovedReason;
        private bool observed,sealedRoot,released;
        internal string CurrentReason=>Data.Retiring?RemovedReason.Value:InitialReason.Value;
        internal override bool IsImmutableCaptureRoot=>sealedRoot;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ArrivalLeaseSnapshot);
        private NavigationArrivalLeaseSnapshot(bool owner):base(Layout){if(owner)Retain();}
        internal static NavigationArrivalLeaseSnapshot AllocatePrepared(INavStorageReservation r,bool owner=true)
        {using(var allocation=new NavStorageAllocation(r.TryReserve)){allocation.Reserve(Layout);return allocation.Register(new NavigationArrivalLeaseSnapshot(owner));}}
        internal void ObservePrepared(NavigationArrivalLeaseValues data,NavDeclarationStorage provider,NavOwnedText binding,NavOwnedText initial,NavOwnedText removed)
        {
            if(observed||sealedRoot||provider==null||!provider.IsImmutableCaptureRoot||!(provider.Facade is ILayeredNavigationProvider)||binding==null||initial==null||removed==null)throw new ArgumentException("Invalid physical lease snapshot roles.");data.Validate();Data=data;ProviderRoot=NavPhysicalMacro.Acquire(this,provider);Binding=NavPhysicalMacro.Acquire(this,binding);InitialReason=NavPhysicalMacro.Acquire(this,initial);RemovedReason=NavPhysicalMacro.Acquire(this,removed);observed=true;
        }
        internal void SealPrepared(INavStorageReservation r)
        {
            if(!observed||sealedRoot||InitialReason.Value!=""||RemovedReason.Value!="gameplay-owner-removed")throw new ArgumentException("Invalid physical lease reason roles.");
            using(NavStorageScratch.Reserve(r,checked(512L+NavContract.DigestScratchBytes(72))))if(Binding.Value!=NavContract.Digest(Data.WriteBinding))throw new ArgumentException("Physical lease binding differs from identity.");sealedRoot=true;
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!sealedRoot||StorageBytes!=NavStorageLayout.Charge(Layout))throw new ArgumentException("Unsealed physical lease snapshot.");Data.Validate();var w=writer.Output;w.Write(Data.Token);w.Write(Data.Acquisition);w.Write(Data.Retirement);w.Write(Data.World);w.Write(Data.Attempt);w.Write(Data.Request);w.Write(Data.Entity);w.Write(Data.Incarnation);w.Write(Data.Order);w.Write(Data.Provider);w.Write(Data.Retiring);
            NavPhysicalMacro.WriteEdge(writer,this,ProviderRoot);NavPhysicalMacro.WriteEdge(writer,this,Binding);NavPhysicalMacro.WriteEdge(writer,this,InitialReason);NavPhysicalMacro.WriteEdge(writer,this,RemovedReason);
        }
        internal static NavigationArrivalLeaseSnapshot ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var r=reader.Input;var data=new NavigationArrivalLeaseValues(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),NavPhysicalMacro.ReadBoolean(reader));data.Validate();var provider=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);var binding=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader);var initial=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader);var removed=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader);var root=AllocatePrepared(reader.Reservation,false);root.ObservePrepared(data,provider,binding,initial,removed);root.SealPrepared(reader.Reservation);return root;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){ProviderRoot?.Release();Binding?.Release();InitialReason?.Release();RemovedReason?.Release();ProviderRoot=null;Binding=InitialReason=RemovedReason=null;}
    }
}

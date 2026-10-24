using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // A host provider entry preserves the concrete provider and geometry pair.
    // Equal binding text alone does not make two entries interchangeable.
    internal sealed class NavigationHostProvider : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long LayoutBytes=Math.Max(256,NavStorageLayout.ObjectBytesBound(typeof(NavigationHostProvider)));
        private NavDeclarationStorage providerRoot;
        private NavGeometry.OwnedBuffers geometryRoot;
        private NavOwnedText surfaceRoot;
        internal ILayeredNavigationProvider Provider {get;private set;}
        internal NavGeometry Geometry {get;private set;}
        internal string SurfaceProviderId=>surfaceRoot.Value;
        internal int LogicalReferences {get;private set;}
        private bool released;
        internal override bool IsImmutableCaptureRoot=>Provider!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostProvider);

        private NavigationHostProvider(bool ownerReference):base(LayoutBytes)
        {if(ownerReference)Retain();}
        private static NavigationHostProvider Allocate(INavStorageReservation reservation,bool ownerReference)
        {
            using(var allocation=new NavStorageAllocationContext(reservation.TryReserve)){
                allocation.Reserve(LayoutBytes);var value=new NavigationHostProvider(ownerReference);allocation.Register(value);return value;
            }
        }

        private void Own(NavDeclarationStorage provider,NavGeometry.OwnedBuffers geometry,NavOwnedText surface)
        {
            providerRoot=NavPhysicalMacro.Acquire(this,provider);
            geometryRoot=NavPhysicalMacro.Acquire(this,geometry);
            surfaceRoot=NavPhysicalMacro.Acquire(this,surface);
        }
        internal static NavigationHostProvider CopyOwned(ILayeredNavigationProvider provider,NavGeometry geometry,
            string surfaceProviderId,int logicalReferences,INavStorageReservation reservation,PartitionNavigationProvider shared=null)
        {
            if(provider==null||geometry==null||string.IsNullOrWhiteSpace(surfaceProviderId)||logicalReferences<1||reservation==null)
                throw new ArgumentException("Invalid host provider entry.");
            var root=(provider as INavStorageFacade)?.StorageRoot;
            if(root!=null)NavStorageAdmission.RequireOwned(reservation,root);
            if(geometry.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,geometry.StorageRoot);
            ILayeredNavigationProvider copy=provider;
            bool newProvider=false,newGeometry=false;
            NavGeometry ownedGeometry=geometry;
            NavOwnedText surface=null;
            var owner=Allocate(reservation,true);
            try{
                if(root==null){
                    if(provider is LayeredNavigationProvider layered)copy=LayeredNavigationProvider.CopyOwned(layered,reservation);
                    else if(provider is PartitionNavigationProvider partition)copy=PartitionNavigationProvider.CopyOwned(partition,reservation,shared);
                    else throw new ArgumentException("Unsupported concrete host provider.");
                    root=(copy as INavStorageFacade)?.StorageRoot;newProvider=true;
                }
                NavStorageAdmission.RequireOwned(reservation,root);
                if(geometry.StorageRoot==null){ownedGeometry=NavGeometry.CopyOwned(geometry,reservation);newGeometry=true;}
                NavStorageAdmission.RequireOwned(reservation,ownedGeometry.StorageRoot);
                surface=NavOwnedText.Copy(surfaceProviderId,reservation);
                owner.Own((NavDeclarationStorage)root,(NavGeometry.OwnedBuffers)ownedGeometry.StorageRoot,surface);
                owner.Provider=copy;owner.Geometry=ownedGeometry;owner.LogicalReferences=logicalReferences;
                return owner;
            }catch{owner.ReleaseStorage();throw;}
            finally{
                if(newProvider)root?.Release();
                if(newGeometry)ownedGeometry.StorageRoot.Release();
                surface?.Release();
            }
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!(providerRoot.Facade is ILayeredNavigationProvider provider)||
                !ReferenceEquals(Provider,provider)||!ReferenceEquals(Geometry,geometryRoot.Facade)||
                surfaceRoot==null||string.IsNullOrWhiteSpace(surfaceRoot.Value)||LogicalReferences<1||
                StorageBytes!=NavStorageLayout.Charge(LayoutBytes))
                throw new ArgumentException("Physical host provider owner contradiction.");
            writer.Output.Write(LogicalReferences);
            NavPhysicalMacro.WriteEdge(writer,this,providerRoot);
            NavPhysicalMacro.WriteEdge(writer,this,geometryRoot);
            NavPhysicalMacro.WriteEdge(writer,this,surfaceRoot);
        }
        internal static NavigationHostProvider ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int count=reader.Input.ReadInt32();
            var provider=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);
            var geometry=NavPhysicalMacro.ReadEdge<NavGeometry.OwnedBuffers>(reader);
            var surface=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader);
            if(count<1||!(provider.Facade is ILayeredNavigationProvider facade)||geometry.Facade==null||
                string.IsNullOrWhiteSpace(surface.Value))throw new ArgumentException("Invalid physical host provider fields.");
            var owner=Allocate(reader.Reservation,false);
            owner.Own(provider,geometry,surface);owner.Provider=facade;owner.Geometry=geometry.Facade;
            owner.LogicalReferences=count;return owner;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){providerRoot?.Release();geometryRoot?.Release();surfaceRoot?.Release();}
    }
}

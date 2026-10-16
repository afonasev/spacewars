using System;

namespace Spacewars.Simulation
{
    internal sealed partial class NavOwnedText : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>true;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Metadata,value:NavPhysicalValueKind.Text);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {writer.Output.Write(StorageBytes-CaptureHeaderBytes);writer.Output.Write(Value.Length);for(int n=0;n<Value.Length;n++)writer.Output.Write((ushort)Value[n]);}
        internal static NavOwnedText ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            long bytes=reader.Input.ReadInt64();int count=reader.Count(16777216);
            bool digest=bytes==280&&count==64;
            if(!digest&&bytes!=160L+2L*count)throw new ArgumentException("Invalid physical metadata layout.");
            using(var scratch=NavStorageScratch.Reserve(reader.Reservation,checked(64L+2L*count)))
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);var chars=new char[count];for(int n=0;n<count;n++)chars[n]=(char)reader.Input.ReadUInt16();
                if(digest)for(int n=0;n<count;n++)if(!(chars[n]>='0'&&chars[n]<='9'||chars[n]>='a'&&chars[n]<='f'))throw new ArgumentException("Invalid physical metadata digest.");
                return allocation.Register(new NavOwnedText(new string(chars),bytes));
            }
        }
    }
    public sealed partial class NavTypedLeg : INavPhysicalBody
    {
        private NavOwnedText ownedFrom,ownedTo,ownedPortal;private NavOwnedStorage ownedProvider;
        private void OwnMetadata(NavOwnedText from,NavOwnedText to,NavOwnedText portal)
        {
            RequireEdge(from);RequireEdge(to);if(portal!=null)RequireEdge(portal);
            from.Retain();ownedFrom=from;to.Retain();ownedTo=to;if(portal!=null){portal.Retain();ownedPortal=portal;}
        }
        internal override bool IsImmutableCaptureRoot=>true;
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Leg);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(ownedProvider!=null){
                var provider=CanonicalProvider(out int from,out int to,out int portal);
                writer.Output.Write(provider is PartitionNavigationProvider?(byte)2:(byte)1);writer.Edge(ownedProvider);WriteScalars(writer);writer.Output.Write(from);writer.Output.Write(to);writer.Output.Write(portal);return;
            }
            writer.Output.Write((byte)0);
            if(ownedFrom==null||ownedTo==null||From.SurfaceId!=ownedFrom.Value||To.SurfaceId!=ownedTo.Value||PortalId!=ownedPortal?.Value)throw new ArgumentException("Unowned typed-leg metadata.");
            WriteScalars(writer);writer.Edge(ownedFrom);writer.Edge(ownedTo);writer.Edge(ownedPortal);
        }
        // Cached graph A may serve a distinct request B with the same canonical binding.
        // Validate the leg's actual selectors against A; never rebind its owning edge to B.
        internal void RequireCanonicalProviderBinding(ILayeredNavigationProvider requestProvider,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            NavStorageAdmission.RequireOwned(reservation,ownedProvider);
            NavStorageAdmission.RequireOwned(reservation,(requestProvider as INavStorageFacade)?.StorageRoot);
            var actual=CanonicalProvider(out _,out _,out _);
            if(actual.GetType()!=requestProvider.GetType()||!string.Equals(actual.Binding,requestProvider.Binding,StringComparison.Ordinal)||Kind!=NavTypedLegKind.Surface&&Kind!=NavTypedLegKind.Portal)throw new ArgumentException("Canonical route/request provider binding changed.");
        }
        // Borrow the exact selector retained by this cached leg. A request may
        // have a distinct provider copy with the same immutable binding.
        internal NavNameReference CanonicalPortalName(ILayeredNavigationProvider requestProvider)
        {
            var actual=CanonicalProvider(out _,out _,out int index);
            if(requestProvider==null||actual.GetType()!=requestProvider.GetType()||
                !string.Equals(actual.Binding,requestProvider.Binding,StringComparison.Ordinal)||
                Kind!=NavTypedLegKind.Portal||index<0)
                throw new ArgumentException("Canonical portal/request provider binding changed.");
            if(actual is PartitionNavigationProvider partition)
                return new NavNameReference(partition.Seams[index].StorageRoot,NavNameRole.SeamId);
            return new NavNameReference(actual.Portals[index].StorageRoot,NavNameRole.PortalId);
        }
        private ILayeredNavigationProvider CanonicalProvider(out int from,out int to,out int portal)
        {
            if(ownedFrom!=null||ownedTo!=null||ownedPortal!=null)throw new ArgumentException("Mixed canonical/private typed-leg metadata.");
            if(!(ownedProvider is NavDeclarationStorage declaration)||!(declaration.Facade is ILayeredNavigationProvider provider)||!(provider is LayeredNavigationProvider)&&!(provider is PartitionNavigationProvider))throw new ArgumentException("Unsupported canonical typed-leg provider BODY.");
            from=SurfaceIndex(provider,From.SurfaceId);to=SurfaceIndex(provider,To.SurfaceId);portal=-1;
            if(!SamePhysicalLocation(From,provider.Surfaces[from].OwnedLocation(From.Position))||!SamePhysicalLocation(To,provider.Surfaces[to].OwnedLocation(To.Position)))throw new ArgumentException("Noncanonical typed-leg surface selector.");
            if(PortalId!=null){
                if(provider is PartitionNavigationProvider partition){for(int n=0;n<partition.Seams.Count;n++)if(ReferenceEquals(partition.Seams[n].Id,PortalId)){portal=n;break;}}
                else for(int n=0;n<provider.Portals.Count;n++)if(ReferenceEquals(provider.Portals[n].Id,PortalId)){portal=n;break;}
                if(portal<0)throw new ArgumentException("Noncanonical typed-leg portal name.");
            }
            if(Kind==NavTypedLegKind.Surface&&portal!=-1||Kind==NavTypedLegKind.Portal&&portal<0)throw new ArgumentException("Canonical typed-leg kind contradiction.");
            if(!ReferenceEquals(((INavStorageFacade)provider).StorageRoot,ownedProvider))throw new ArgumentException("Canonical typed-leg facade owner changed.");
            return provider;
        }
        private void WriteScalars(NavPhysicalBodyWriter writer)
        {writer.Output.Write(From.Position.X);writer.Output.Write(From.Position.Z);writer.Output.Write(To.Position.X);writer.Output.Write(To.Position.Z);writer.Output.Write(Cost);writer.Output.Write((int)Kind);}
        private static int SurfaceIndex(ILayeredNavigationProvider provider,string name)
        {for(int n=0;n<provider.Surfaces.Count;n++)if(ReferenceEquals(provider.Surfaces[n].Id,name))return n;throw new ArgumentException("Noncanonical typed-leg surface name.");}
        internal bool HasCanonicalProvider(NavOwnedStorage provider)=>ownedProvider==provider&&ownedFrom==null&&ownedTo==null&&ownedPortal==null;
        internal static bool SamePhysicalLocation(NavLocation a,NavLocation b)=>a.Position.Equals(b.Position)&&ReferenceEquals(a.SurfaceId,b.SurfaceId)&&a.NameReference.Owner==b.NameReference.Owner&&a.NameReference.Role==b.NameReference.Role&&a.NameReference.Index==b.NameReference.Index;
        internal static NavTypedLeg ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            byte mode=reader.Input.ReadByte();if(mode>2)throw new ArgumentException("Unknown physical typed-leg name mode.");
            NavDeclarationStorage owner=mode!=0?reader.Edge<NavDeclarationStorage>():null;
            if(mode==1&&!(owner?.Facade is LayeredNavigationProvider)||mode==2&&!(owner?.Facade is PartitionNavigationProvider))throw new ArgumentException("Wrong canonical typed-leg provider.");
            var fromPoint=new NavPoint(reader.Input.ReadDouble(),reader.Input.ReadDouble());var toPoint=new NavPoint(reader.Input.ReadDouble(),reader.Input.ReadDouble());
            double cost=reader.Input.ReadDouble();var kind=(NavTypedLegKind)reader.Input.ReadInt32();
            if(double.IsNaN(cost)||double.IsInfinity(cost)||cost<0||!Enum.IsDefined(typeof(NavTypedLegKind),kind))throw new ArgumentException("Invalid physical typed leg.");
            if(mode!=0){
                var provider=(ILayeredNavigationProvider)owner.Facade;int fromIndex=reader.Input.ReadInt32(),toIndex=reader.Input.ReadInt32(),portalIndex=reader.Input.ReadInt32();
                if(fromIndex<0||fromIndex>=provider.Surfaces.Count||toIndex<0||toIndex>=provider.Surfaces.Count||portalIndex<-1||portalIndex>=(provider is PartitionNavigationProvider partition?partition.Seams.Count:provider.Portals.Count)||kind==NavTypedLegKind.Surface&&portalIndex!=-1||kind==NavTypedLegKind.Portal&&portalIndex<0)throw new ArgumentException("Canonical typed-leg name/index contradiction.");
                var canonicalFrom=provider.Surfaces[fromIndex].OwnedLocation(fromPoint);var canonicalTo=provider.Surfaces[toIndex].OwnedLocation(toPoint);string portalName=portalIndex<0?null:provider is PartitionNavigationProvider partitionProvider?partitionProvider.Seams[portalIndex].Id:provider.Portals[portalIndex].Id;
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                    allocation.Reserve(StorageCharge);var result=allocation.Register(new NavTypedLeg(canonicalFrom,canonicalTo,cost,kind,portalName,true));result.RequireEdge(owner);owner.Retain();result.ownedProvider=owner;return result;
                }
            }
            var from=reader.Edge<NavOwnedText>();var to=reader.Edge<NavOwnedText>();var portal=reader.Edge<NavOwnedText>();
            if(from==null||to==null||kind==NavTypedLegKind.Surface&&portal!=null||kind==NavTypedLegKind.Portal&&string.IsNullOrWhiteSpace(portal?.Value))throw new ArgumentException("Typed-leg metadata/kind contradiction.");
            var fromLocation=new NavLocation(fromPoint,new NavNameReference(from,NavNameRole.Text));var toLocation=new NavLocation(toPoint,new NavNameReference(to,NavNameRole.Text));
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(StorageCharge);var result=allocation.Register(new NavTypedLeg(fromLocation,toLocation,cost,kind,portal?.Value,true));result.OwnMetadata(from,to,portal);return result;
            }
        }
    }
}

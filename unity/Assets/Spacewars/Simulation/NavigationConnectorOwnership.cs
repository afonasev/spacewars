using System;

namespace Spacewars.Simulation
{
    internal enum NavConnectorNameRole : byte { Private=0,ProviderBinding,SurfaceId,PortalId,PortalEntry,PortalExit,Null,PrivateAlias }
    internal readonly struct NavConnectorName
    {
        internal readonly NavConnectorNameRole Role;internal readonly int Index;
        internal NavConnectorName(NavConnectorNameRole role,int index=-1){Role=role;Index=index;}
        internal string Resolve(ILayeredNavigationProvider provider)
        {
            if(provider==null)throw new ArgumentException("Missing connector name provider.");
            if(Role==NavConnectorNameRole.ProviderBinding){if(Index!=-1)throw new ArgumentException("Invalid binding role index.");return provider.Binding;}
            if(Role==NavConnectorNameRole.SurfaceId){if(Index<0||Index>=provider.Surfaces.Count)throw new ArgumentException("Invalid surface role index.");return provider.Surfaces[Index].Id;}
            if(Index<0||Index>=provider.Portals.Count)throw new ArgumentException("Invalid portal role index.");
            var p=provider.Portals[Index];switch(Role){case NavConnectorNameRole.PortalId:return p.Id;case NavConnectorNameRole.PortalEntry:return p.Entry.SurfaceId;case NavConnectorNameRole.PortalExit:return p.Exit.SurfaceId;default:throw new ArgumentException("Wrong connector name role.");}
        }
        internal static NavConnectorName Describe(string text,ILayeredNavigationProvider provider)
        {
            if(text==null)return new NavConnectorName(NavConnectorNameRole.Null);
            if(provider!=null){
                if(ReferenceEquals(text,provider.Binding))return new NavConnectorName(NavConnectorNameRole.ProviderBinding);
                for(int n=0;n<provider.Portals.Count;n++){var p=provider.Portals[n];if(ReferenceEquals(text,p.Id))return new NavConnectorName(NavConnectorNameRole.PortalId,n);if(ReferenceEquals(text,p.Entry.SurfaceId))return new NavConnectorName(NavConnectorNameRole.PortalEntry,n);if(ReferenceEquals(text,p.Exit.SurfaceId))return new NavConnectorName(NavConnectorNameRole.PortalExit,n);}
                for(int n=0;n<provider.Surfaces.Count;n++)if(ReferenceEquals(text,provider.Surfaces[n].Id))return new NavConnectorName(NavConnectorNameRole.SurfaceId,n);
            }
            NavContract.Text(text);return new NavConnectorName(NavConnectorNameRole.Private);
        }
    }
    public sealed partial class NavConnectorResult : INavPhysicalBody
    {
        private NavOwnedStorage ownedProvider;
        private NavConnectorName[] ownedNames;
        private NavConnectorResult(long bytes,NavConnectorStatus status,string[] names,NavPoint origin,NavPoint entry,NavPoint exit,NavPoint endpoint,double cost):base(bytes)
        {Status=status;ProviderBinding=names[0];PortalId=names[1];Origin=Location(origin,names[2]);Entry=Location(entry,names[3]);Exit=Location(exit,names[4]);Endpoint=Location(endpoint,names[5]);Cost=cost;}
        internal override bool IsImmutableCaptureRoot=>ownedNames!=null;
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Connector);
        private string Name(int n){switch(n){case 0:return ProviderBinding;case 1:return PortalId;case 2:return Origin.SurfaceId;case 3:return Entry.SurfaceId;case 4:return Exit.SurfaceId;case 5:return Endpoint.SurfaceId;default:throw new ArgumentOutOfRangeException();}}
        private static void Validate(NavConnectorStatus status,string binding,NavPoint origin,NavPoint entry,NavPoint exit,NavPoint endpoint,double cost)
        {if(!Enum.IsDefined(typeof(NavConnectorStatus),status))throw new ArgumentException("Invalid connector status.");NavContract.Sha256(binding);NavContract.Number(cost);Point(origin);Point(entry);Point(exit);Point(endpoint);}
        private static NavLocation Location(NavPoint p,string name){if(name!=null)return new NavLocation(p,name);if(!p.Equals(default(NavPoint)))throw new ArgumentException("Null connector location has non-default point.");return default;}
        private static void Point(NavPoint p){if(double.IsNaN(p.X)||double.IsInfinity(p.X)||double.IsNaN(p.Z)||double.IsInfinity(p.Z))throw new ArgumentException("Invalid connector point.");}
        internal static NavConnectorResult CreatePhysical(NavConnectorStatus status,string binding,string portalId,NavLocation origin,NavLocation entry,NavLocation exit,NavLocation endpoint,double cost,INavStorageReservation reservation,ILayeredNavigationProvider provider)
        {
            var providerRoot=(provider as INavStorageFacade)?.StorageRoot;
            if(provider!=null)NavStorageAdmission.RequireOwned(reservation,providerRoot);
            origin.RequireName(reservation);entry.RequireName(reservation);exit.RequireName(reservation);endpoint.RequireName(reservation);
            Validate(status,binding,origin.Position,entry.Position,exit.Position,endpoint.Position,cost);
            using(var scratch=NavStorageScratch.Reserve(reservation,2048)){
                var source=new[]{binding,portalId,origin.SurfaceId,entry.SurfaceId,exit.SurfaceId,endpoint.SurfaceId};var plan=new NavConnectorName[6];long bytes=StorageCharge+96;
                for(int n=0;n<6;n++){
                    plan[n]=NavConnectorName.Describe(source[n],provider);
                    if(plan[n].Role!=NavConnectorNameRole.Private)continue;
                    for(int k=0;k<n;k++)if(ReferenceEquals(source[n],source[k])&&(plan[k].Role==NavConnectorNameRole.Private||plan[k].Role==NavConnectorNameRole.PrivateAlias)){plan[n]=new NavConnectorName(NavConnectorNameRole.PrivateAlias,k);break;}
                    if(plan[n].Role==NavConnectorNameRole.Private)bytes=checked(bytes+NavProviderCopy.TextBytes(source[n]));
                }
                using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                    allocation.Reserve(bytes);var names=new string[6];var roles=(NavConnectorName[])plan.Clone();
                    for(int n=0;n<6;n++)names[n]=plan[n].Role==NavConnectorNameRole.Private?NavProviderCopy.Text(source[n]):plan[n].Role==NavConnectorNameRole.PrivateAlias?names[plan[n].Index]:source[n];
                    var result=allocation.Register(new NavConnectorResult(bytes,status,names,origin.Position,entry.Position,exit.Position,endpoint.Position,cost){ownedNames=roles});
                    if(providerRoot!=null){providerRoot.Retain();result.ownedProvider=providerRoot;}result.BindOwnedLocations(provider);result.Retain();return result;
                }
            }
        }
        private void BindOwnedLocations(ILayeredNavigationProvider provider)
        {
            NavLocation Bind(NavLocation location,int slot){
                var field=ownedNames[slot];if(field.Role==NavConnectorNameRole.Null)return default;
                NavNameReference name;
                switch(field.Role){
                    case NavConnectorNameRole.Private:case NavConnectorNameRole.PrivateAlias:name=new NavNameReference(this,NavNameRole.ConnectorPrivateSlot,slot);break;
                    case NavConnectorNameRole.SurfaceId:name=new NavNameReference(provider.Surfaces[field.Index].StorageRoot,NavNameRole.SurfaceId);break;
                    case NavConnectorNameRole.PortalEntry:name=new NavNameReference(provider.Portals[field.Index].StorageRoot,NavNameRole.PortalEntrySurface);break;
                    case NavConnectorNameRole.PortalExit:name=new NavNameReference(provider.Portals[field.Index].StorageRoot,NavNameRole.PortalExitSurface);break;
                    case NavConnectorNameRole.PortalId:name=new NavNameReference(provider.Portals[field.Index].StorageRoot,NavNameRole.PortalId);break;
                    case NavConnectorNameRole.ProviderBinding:name=new NavNameReference(ownedProvider,NavNameRole.ProviderBinding);break;
                    default:throw new ArgumentException("Unknown connector location role.");
                }
                if(!ReferenceEquals(location.SurfaceId,name.Resolve()))throw new ArgumentException("Connector location name identity contradiction.");return new NavLocation(location.Position,name);
            }
            Origin=Bind(Origin,2);Entry=Bind(Entry,3);Exit=Bind(Exit,4);Endpoint=Bind(Endpoint,5);
        }
        internal void RequireQualification(ILayeredNavigationProvider provider,int portalIndex)
        {
            if(ownedProvider!=(provider as INavStorageFacade)?.StorageRoot||ownedNames==null||ownedNames.Length!=6)throw new ArgumentException("Graph qualification provider contradiction.");
            var portal=provider.Portals[portalIndex];
            for(int n=0;n<6;n++){
                var role=n==0?NavConnectorNameRole.ProviderBinding:n==1?NavConnectorNameRole.PortalId:n<4?NavConnectorNameRole.PortalEntry:NavConnectorNameRole.PortalExit;
                if(ownedNames[n].Role!=role||ownedNames[n].Index!=(n==0?-1:portalIndex)||!ReferenceEquals(Name(n),ownedNames[n].Resolve(provider)))throw new ArgumentException("Graph qualification name-role contradiction.");
            }
            if(!NavTypedLeg.SamePhysicalLocation(Origin,portal.Entry)||!NavTypedLeg.SamePhysicalLocation(Entry,portal.Entry)||!NavTypedLeg.SamePhysicalLocation(Exit,portal.Exit)||!NavTypedLeg.SamePhysicalLocation(Endpoint,portal.Exit))throw new ArgumentException("Graph qualification endpoint selector contradiction.");
        }
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(ownedNames==null||ownedNames.Length!=6)throw new ArgumentException("Unowned connector metadata.");
            Validate(Status,ProviderBinding,Origin.Position,Entry.Position,Exit.Position,Endpoint.Position,Cost);var provider=(ownedProvider as NavDeclarationStorage)?.Facade as ILayeredNavigationProvider;long bytes=StorageCharge+96;
            for(int n=0;n<6;n++){
                var role=ownedNames[n];writer.Output.Write((byte)role.Role);writer.Output.Write(role.Index);
                switch(role.Role){
                    case NavConnectorNameRole.Private:if(role.Index!=-1)throw new ArgumentException("Invalid private connector name index.");NavPhysicalTextPlan.WritePlan(writer,Name(n));bytes=checked(bytes+NavProviderCopy.TextBytes(Name(n)));break;
                    case NavConnectorNameRole.Null:if(role.Index!=-1||Name(n)!=null)throw new ArgumentException("Invalid null connector name.");break;
                    case NavConnectorNameRole.PrivateAlias:if(role.Index<0||role.Index>=n||(ownedNames[role.Index].Role!=NavConnectorNameRole.Private&&ownedNames[role.Index].Role!=NavConnectorNameRole.PrivateAlias)||!ReferenceEquals(Name(n),Name(role.Index)))throw new ArgumentException("Invalid private connector alias.");break;
                    default:if(!ReferenceEquals(Name(n),role.Resolve(provider)))throw new ArgumentException("Connector name owner-role contradiction.");break;
                }
            }
            if(StorageBytes!=NavStorageLayout.Charge(bytes))throw new ArgumentException("Connector layout contradiction.");writer.Edge(ownedProvider);
            for(int n=0;n<6;n++)if(ownedNames[n].Role==NavConnectorNameRole.Private)NavPhysicalTextPlan.WriteData(writer,Name(n));
            writer.Output.Write((int)Status);NavContract.Point(writer.Output,Origin.Position);NavContract.Point(writer.Output,Entry.Position);NavContract.Point(writer.Output,Exit.Position);NavContract.Point(writer.Output,Endpoint.Position);writer.Output.Write(Cost);
        }
        internal static NavConnectorResult ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            using(var scratch=NavStorageScratch.Reserve(reader.Reservation,2048)){
                var roles=new NavConnectorName[6];var plans=new NavPhysicalTextPlan[6];long bytes=StorageCharge+96;bool canonical=false;
                for(int n=0;n<6;n++){
                    var role=(NavConnectorNameRole)reader.Input.ReadByte();int index=reader.Input.ReadInt32();if(!Enum.IsDefined(typeof(NavConnectorNameRole),role))throw new ArgumentException("Unknown connector metadata role.");roles[n]=new NavConnectorName(role,index);
                    if(role==NavConnectorNameRole.Private){if(index!=-1)throw new ArgumentException("Invalid private connector role index.");plans[n]=NavPhysicalTextPlan.Read(reader);bytes=checked(bytes+plans[n].Bytes);}
                    else if(role==NavConnectorNameRole.Null){if(index!=-1)throw new ArgumentException("Invalid null connector role index.");}
                    else if(role==NavConnectorNameRole.PrivateAlias){if(index<0||index>=n||(roles[index].Role!=NavConnectorNameRole.Private&&roles[index].Role!=NavConnectorNameRole.PrivateAlias))throw new ArgumentException("Invalid connector name alias/cycle.");}
                    else canonical=true;
                }
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                    allocation.Reserve(bytes);var providerRoot=reader.Edge<NavDeclarationStorage>();var provider=providerRoot?.Facade as ILayeredNavigationProvider;if(canonical&&provider==null||providerRoot!=null&&provider==null)throw new ArgumentException("Wrong connector provider BODY.");
                    var names=new string[6];for(int n=0;n<6;n++)names[n]=roles[n].Role==NavConnectorNameRole.Private?plans[n].ReadData(reader):roles[n].Role==NavConnectorNameRole.Null?null:roles[n].Role==NavConnectorNameRole.PrivateAlias?names[roles[n].Index]:roles[n].Resolve(provider);
                    var status=(NavConnectorStatus)reader.Input.ReadInt32();var origin=NavPhysicalGeometryValue.Point(reader);var entry=NavPhysicalGeometryValue.Point(reader);var exit=NavPhysicalGeometryValue.Point(reader);var endpoint=NavPhysicalGeometryValue.Point(reader);double cost=reader.Input.ReadDouble();Validate(status,names[0],origin,entry,exit,endpoint,cost);
                    var result=allocation.Register(new NavConnectorResult(bytes,status,names,origin,entry,exit,endpoint,cost){ownedNames=(NavConnectorName[])roles.Clone()});if(providerRoot!=null){result.RequireEdge(providerRoot);providerRoot.Retain();result.ownedProvider=providerRoot;}result.BindOwnedLocations(provider);return result;
                }
            }
        }
        protected override void ReleaseEdges(){ownedProvider?.Release();}
    }
}

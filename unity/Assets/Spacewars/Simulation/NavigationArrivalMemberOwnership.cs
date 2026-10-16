using System;

namespace Spacewars.Simulation
{
    public sealed partial class NavArrivalMember : INavStorageFacade
    {
        private NavDeclarationStorage ownedMember;
        NavOwnedStorage INavStorageFacade.StorageRoot=>ownedMember;
        internal NavOwnedStorage StorageRoot=>ownedMember;
        private static readonly long LayoutBase=Math.Max(512,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavArrivalMember))+160);
        private static long BindingWire(NavLocation origin,NavClearanceProfile clearance,string hold)=>checked(96L+NavContract.LocationWireBytes(origin)+NavContract.StringWireBytes(clearance.Id)+4L+NavContract.StringWireBytes(clearance.MobilityClass)+12L+1L+(hold==null?0:NavContract.StringWireBytes(hold)));
        internal static NavArrivalMember CopyOwned(NavArrivalMember source,NavClearanceProfile clearance,INavStorageReservation reservation)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));NavMetadataOwnership.Require(clearance,reservation);source.Origin.RequireName(reservation);if(source.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,source.StorageRoot);
            if(source.Clearance.MetadataOwner!=null){NavMetadataOwnership.Require(source.Clearance,reservation);if(!ReferenceEquals(source.Clearance,clearance))throw new ArgumentException("Owned arrival clearance must retain its exact selector.");}
            bool ownName=source.Origin.NameReference.Owner==null;long bytes=checked(LayoutBase+NavProviderCopy.TextBytes(source.HoldBinding)+(ownName?NavProviderCopy.TextBytes(source.Origin.SurfaceId):0));
            using(NavStorageScratch.Reserve(reservation,8192+NavContract.DigestScratchBytes(BindingWire(source.Origin,clearance,source.HoldBinding))))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(bytes);var origin=ownName?NavProviderCopy.Location(source.Origin):source.Origin;
                var value=new NavArrivalMember(source.EntityId,source.Incarnation,source.Order,source.MobilityRevision,origin,clearance,source.WorldGeneration,source.GroupId,source.RootOrderRevision,source.CommandSequence,source.IssuedTick,source.ActivationRevision,source.MemberOrderRevision,source.RequestSequence,NavProviderCopy.Text(source.HoldBinding));if(value.StableKey!=source.StableKey)throw new ArgumentException("Owned arrival member identity changed.");
                var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.First=NavPhysicalMacro.Acquire(storage,clearance.MetadataOwner);storage.Second=NavPhysicalMacro.Acquire(storage,source.Origin.NameReference.Owner);value.ownedMember=storage;storage.Facade=value;if(ownName)value.Origin=new NavLocation(origin.Position,new NavNameReference(storage,NavNameRole.ArrivalMemberOriginSurface));storage.Retain();return value;
            }
        }
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            bool ownName=ReferenceEquals(Origin.NameReference.Owner,storage);
            if(!ReferenceEquals(storage,ownedMember)||!ReferenceEquals(storage.First,Clearance.MetadataOwner)||!ReferenceEquals(storage.Second,ownName?null:Origin.NameReference.Owner)||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null||storage.StorageBytes!=NavStorageLayout.Charge(LayoutBase+NavProviderCopy.TextBytes(HoldBinding)+(ownName?NavProviderCopy.TextBytes(Origin.SurfaceId):0))||ownName&&(Origin.NameReference.Role!=NavNameRole.ArrivalMemberOriginSurface||Origin.NameReference.Index!=-1))throw new ArgumentException("Physical arrival member owner/layout/role contradiction.");
            Origin.NameReference.Validate(Origin.SurfaceId,storage);writer.Output.Write(ownName);if(ownName)NavPhysicalTextPlan.WritePlan(writer,Origin.SurfaceId);writer.Output.Write(HoldBinding!=null);if(HoldBinding!=null)writer.Output.Write(HoldBinding.Length);NavPhysicalMacro.WriteMetadata(writer,storage,Clearance);
            if(ownName){NavContract.Point(writer.Output,Origin.Position);NavPhysicalTextPlan.WriteData(writer,Origin.SurfaceId);}else NavPhysicalNamedValue.WriteLocation(writer,Origin);
            if(HoldBinding!=null)NavPhysicalTextPlan.WriteData(writer,HoldBinding);
            foreach(long value in new[]{EntityId,Incarnation,Order,MobilityRevision,WorldGeneration,GroupId,RootOrderRevision,CommandSequence,IssuedTick,ActivationRevision,MemberOrderRevision,RequestSequence})writer.Output.Write(value);
            NavPhysicalTextPlan.WriteData(writer,NavContract.Sha256(StableKey));
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            bool ownName=NavPhysicalMacro.ReadBoolean(reader);var name=ownName?NavPhysicalTextPlan.Read(reader):default;bool holding=NavPhysicalMacro.ReadBoolean(reader);var hold=holding?NavPhysicalTextPlan.Read(reader):default;var clearance=NavPhysicalMacro.ReadMetadata<NavClearanceProfile>(reader,NavMetadataRole.Clearance);
            long bytes=checked(LayoutBase+(holding?hold.Bytes:0)+(ownName?name.Bytes:0));
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);
                using(NavStorageScratch.Reserve(reader.Reservation,8192)){
                    var origin=ownName?new NavLocation(NavPhysicalGeometryValue.Point(reader),name.ReadData(reader)):NavPhysicalNamedValue.ReadLocation(reader);if(!ownName&&origin.NameReference.Owner==null)throw new ArgumentException("Unqualified external arrival origin.");string binding=holding?hold.ReadRawData(reader):null;
                    long entity=reader.Input.ReadInt64(),incarnation=reader.Input.ReadInt64(),order=reader.Input.ReadInt64(),mobility=reader.Input.ReadInt64(),world=reader.Input.ReadInt64(),group=reader.Input.ReadInt64(),root=reader.Input.ReadInt64(),command=reader.Input.ReadInt64(),issued=reader.Input.ReadInt64(),activation=reader.Input.ReadInt64(),memberOrder=reader.Input.ReadInt64(),request=reader.Input.ReadInt64();var chars=new char[64];for(int n=0;n<64;n++)chars[n]=(char)reader.Input.ReadUInt16();string expected=NavContract.Sha256(new string(chars));
                    using(NavStorageScratch.Reserve(reader.Reservation,NavContract.DigestScratchBytes(BindingWire(origin,clearance,binding)))){
                        var value=new NavArrivalMember(entity,incarnation,order,mobility,origin,clearance,world,group,root,command,issued,activation,memberOrder,request,binding);if(value.StableKey!=expected)throw new ArgumentException("Physical arrival member digest mismatch.");
                        var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.First=NavPhysicalMacro.Acquire(storage,clearance.MetadataOwner);storage.Second=NavPhysicalMacro.Acquire(storage,origin.NameReference.Owner);value.ownedMember=storage;storage.Facade=value;if(ownName)value.Origin=new NavLocation(origin.Position,new NavNameReference(storage,NavNameRole.ArrivalMemberOriginSurface));return storage;
                    }
                }
            }
        }
    }
}

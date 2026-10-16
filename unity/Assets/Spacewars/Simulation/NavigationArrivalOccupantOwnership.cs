using System;
namespace Spacewars.Simulation
{
    public sealed partial class NavArrivalOccupant : INavStorageFacade
    {
        private NavDeclarationStorage ownedOccupant;
        NavOwnedStorage INavStorageFacade.StorageRoot=>ownedOccupant;
        internal NavOwnedStorage StorageRoot=>ownedOccupant;
        private static readonly long LayoutBase=Math.Max(512,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavArrivalOccupant))+160);
        private static long Wire(NavLocation location,string identity,NavLocation? target)=>checked(96+NavContract.LocationWireBytes(location)+NavContract.StringWireBytes(identity)+(target.HasValue?NavContract.LocationWireBytes(target.Value):0));
        internal static NavArrivalOccupant CopyOwned(NavArrivalOccupant source,INavStorageReservation reservation)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));source.Location.RequireName(reservation);if(source.ActorTarget.HasValue)source.ActorTarget.Value.RequireName(reservation);if(source.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,source.StorageRoot);
            bool own=source.Location.NameReference.Owner==null,ownTarget=source.ActorTarget.HasValue&&source.ActorTarget.Value.NameReference.Owner==null;
            long bytes=checked(LayoutBase+NavProviderCopy.TextBytes(source.Identity)+(own?NavProviderCopy.TextBytes(source.Location.SurfaceId):0)+(ownTarget?NavProviderCopy.TextBytes(source.ActorTarget.Value.SurfaceId):0));
            using(NavStorageScratch.Reserve(reservation,8192+NavContract.DigestScratchBytes(Wire(source.Location,source.Identity,source.ActorTarget))))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(bytes);var location=own?NavProviderCopy.Location(source.Location):source.Location;NavLocation? target=source.ActorTarget.HasValue?(ownTarget?NavProviderCopy.Location(source.ActorTarget.Value):source.ActorTarget.Value):(NavLocation?)null;
                var value=new NavArrivalOccupant(source.Kind,source.ActorId,source.Incarnation,location,source.Radius,source.BlocksPlacement,NavProviderCopy.Text(source.Identity),source.ReservationKind,source.RequestSequence,source.HistoricalExecution,target);if(value.StableKey!=source.StableKey)throw new ArgumentException("Owned arrival occupancy identity changed.");
                var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.First=NavPhysicalMacro.Acquire(storage,source.Location.NameReference.Owner);storage.Second=NavPhysicalMacro.Acquire(storage,source.ActorTarget?.NameReference.Owner);value.ownedOccupant=storage;storage.Facade=value;value.BindOwnNames(own,ownTarget);storage.Retain();return value;
            }
        }
        private void BindOwnNames(bool own,bool ownTarget)
        {if(own)Location=new NavLocation(Location.Position,new NavNameReference(ownedOccupant,NavNameRole.ArrivalOccupantSurface));if(ownTarget)ActorTarget=new NavLocation(ActorTarget.Value.Position,new NavNameReference(ownedOccupant,NavNameRole.ArrivalOccupantTargetSurface));}
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            bool own=ReferenceEquals(Location.NameReference.Owner,storage),hasTarget=ActorTarget.HasValue,ownTarget=hasTarget&&ReferenceEquals(ActorTarget.Value.NameReference.Owner,storage);
            long bytes=checked(LayoutBase+NavProviderCopy.TextBytes(Identity)+(own?NavProviderCopy.TextBytes(Location.SurfaceId):0)+(ownTarget?NavProviderCopy.TextBytes(ActorTarget.Value.SurfaceId):0));
            if(!ReferenceEquals(storage,ownedOccupant)||!ReferenceEquals(storage.First,own?null:Location.NameReference.Owner)||!ReferenceEquals(storage.Second,ownTarget?null:ActorTarget?.NameReference.Owner)||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null||storage.StorageBytes!=NavStorageLayout.Charge(bytes)||own&&(Location.NameReference.Role!=NavNameRole.ArrivalOccupantSurface||Location.NameReference.Index!=-1)||ownTarget&&(ActorTarget.Value.NameReference.Role!=NavNameRole.ArrivalOccupantTargetSurface||ActorTarget.Value.NameReference.Index!=-1))throw new ArgumentException("Physical arrival occupancy owner/layout contradiction.");
            Location.NameReference.Validate(Location.SurfaceId,storage);if(hasTarget)ActorTarget.Value.NameReference.Validate(ActorTarget.Value.SurfaceId,storage);
            writer.Output.Write(own);if(own)NavPhysicalTextPlan.WritePlan(writer,Location.SurfaceId);writer.Output.Write(hasTarget);if(hasTarget){writer.Output.Write(ownTarget);if(ownTarget)NavPhysicalTextPlan.WritePlan(writer,ActorTarget.Value.SurfaceId);}NavPhysicalTextPlan.WritePlan(writer,Identity);
            if(own){NavContract.Point(writer.Output,Location.Position);NavPhysicalTextPlan.WriteData(writer,Location.SurfaceId);}else NavPhysicalNamedValue.WriteLocation(writer,Location);
            if(hasTarget){if(ownTarget){NavContract.Point(writer.Output,ActorTarget.Value.Position);NavPhysicalTextPlan.WriteData(writer,ActorTarget.Value.SurfaceId);}else NavPhysicalNamedValue.WriteLocation(writer,ActorTarget.Value);}
            NavPhysicalTextPlan.WriteData(writer,Identity);writer.Output.Write((int)Kind);writer.Output.Write(ActorId);writer.Output.Write(Incarnation);writer.Output.Write(Radius);writer.Output.Write(BlocksPlacement);writer.Output.Write(ReservationKind.HasValue);if(ReservationKind.HasValue)writer.Output.Write((int)ReservationKind.Value);writer.Output.Write(RequestSequence);writer.Output.Write(HistoricalExecution);NavPhysicalTextPlan.WriteData(writer,NavContract.Sha256(StableKey));
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            bool own=NavPhysicalMacro.ReadBoolean(reader);var locationPlan=own?NavPhysicalTextPlan.Read(reader):default;bool hasTarget=NavPhysicalMacro.ReadBoolean(reader),ownTarget=hasTarget&&NavPhysicalMacro.ReadBoolean(reader);var targetPlan=ownTarget?NavPhysicalTextPlan.Read(reader):default;var identityPlan=NavPhysicalTextPlan.Read(reader);
            long bytes=checked(LayoutBase+identityPlan.Bytes+(own?locationPlan.Bytes:0)+(ownTarget?targetPlan.Bytes:0));
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);
                using(NavStorageScratch.Reserve(reader.Reservation,8192)){
                    var location=own?new NavLocation(NavPhysicalGeometryValue.Point(reader),locationPlan.ReadData(reader)):NavPhysicalNamedValue.ReadLocation(reader);NavLocation? target=hasTarget?(ownTarget?new NavLocation(NavPhysicalGeometryValue.Point(reader),targetPlan.ReadData(reader)):NavPhysicalNamedValue.ReadLocation(reader)):(NavLocation?)null;
                    if(!own&&location.NameReference.Owner==null||hasTarget&&!ownTarget&&target.Value.NameReference.Owner==null)throw new ArgumentException("Unqualified external occupancy location.");
                    string identity=identityPlan.ReadData(reader);var kind=(NavArrivalOccupantKind)reader.Input.ReadInt32();long actor=reader.Input.ReadInt64(),incarnation=reader.Input.ReadInt64();double radius=reader.Input.ReadDouble();bool blocks=NavPhysicalMacro.ReadBoolean(reader),hasReservation=NavPhysicalMacro.ReadBoolean(reader);NavArrivalReservationKind? reservation=hasReservation?(NavArrivalReservationKind)reader.Input.ReadInt32():(NavArrivalReservationKind?)null;long request=reader.Input.ReadInt64(),historical=reader.Input.ReadInt64();var chars=new char[64];for(int n=0;n<64;n++)chars[n]=(char)reader.Input.ReadUInt16();string expected=NavContract.Sha256(new string(chars));
                    using(NavStorageScratch.Reserve(reader.Reservation,NavContract.DigestScratchBytes(Wire(location,identity,target)))){
                        var value=new NavArrivalOccupant(kind,actor,incarnation,location,radius,blocks,identity,reservation,request,historical,target);if(value.StableKey!=expected)throw new ArgumentException("Physical occupancy digest mismatch.");var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.First=NavPhysicalMacro.Acquire(storage,location.NameReference.Owner);storage.Second=NavPhysicalMacro.Acquire(storage,target?.NameReference.Owner);value.ownedOccupant=storage;storage.Facade=value;value.BindOwnNames(own,ownTarget);return storage;
                    }
                }
            }
        }
    }
}

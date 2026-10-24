using System;
namespace Spacewars.Simulation
{
    public sealed partial class NavigationProfile : INavStorageFacade
    {
        private NavDeclarationStorage ownedProfile;
        NavOwnedStorage INavStorageFacade.StorageRoot=>ownedProfile;
        internal NavOwnedStorage StorageRoot=>ownedProfile;
        private static readonly long LayoutBase=Math.Max(384,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavigationProfile)));
        internal static NavigationProfile CopyOwned(NavigationProfile source,INavStorageReservation reservation)
        {if(source==null)throw new ArgumentNullException(nameof(source));return CreateForUnitOwned(source,source.Radius,source.TankSpeed,source.TankTurnSpeed,reservation);}
        internal static NavigationProfile CreateForUnitOwned(NavigationProfile source,double radius,double speed,double turn,INavStorageReservation reservation)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));if(reservation==null)throw new ArgumentNullException(nameof(reservation));if(source.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,source.StorageRoot);
            long bytes=checked(LayoutBase+NavProviderCopy.TextBytes(source.ProfileId));
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(bytes);var value=new NavigationProfile(NavProviderCopy.Text(source.ProfileId),source.Revision,radius,speed,turn,source.GridCell,source.ArrivalTolerance,source.LocalDetourDistance,source.ForwardAlignmentAngle,source.CameraOrthoSize,source.CameraHeight,source.CameraOffsetZ,source.RepairRetrySeconds,source.ArrivalSlotSpacing,source.BlockedTimeoutSeconds,source.LocalGridCell,source.CrowdHeadingIntervals,source.CrowdSpeedBands,source.CrowdLookaheadSeconds,source.CrowdNeighborCap,source.GroupCorridorMaximumExcessPercent);var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.Facade=value;value.ownedProfile=storage;storage.Retain();return value;
            }
        }
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            if(!ReferenceEquals(storage,ownedProfile)||storage.First!=null||storage.Second!=null||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null||storage.StorageBytes!=NavStorageLayout.Charge(LayoutBase+NavProviderCopy.TextBytes(ProfileId)))throw new ArgumentException("Physical arrival profile owner/layout contradiction.");
            NavPhysicalTextPlan.WritePlan(writer,ProfileId);NavPhysicalTextPlan.WriteData(writer,ProfileId);writer.Output.Write(Revision);
            writer.Output.Write(Radius);writer.Output.Write(TankSpeed);writer.Output.Write(TankTurnSpeed);writer.Output.Write(GridCell);writer.Output.Write(ArrivalTolerance);writer.Output.Write(LocalDetourDistance);writer.Output.Write(ForwardAlignmentAngle);writer.Output.Write(CameraOrthoSize);writer.Output.Write(CameraHeight);writer.Output.Write(CameraOffsetZ);writer.Output.Write(RepairRetrySeconds);writer.Output.Write(ArrivalSlotSpacing);writer.Output.Write(BlockedTimeoutSeconds);writer.Output.Write(LocalGridCell);if(!HasCrowdV1Defaults||GroupCorridorMaximumExcessPercent!=0)WriteCrowdValues(writer.Output);if(GroupCorridorMaximumExcessPercent!=0)writer.Output.Write(GroupCorridorMaximumExcessPercent);
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader,bool crowdExtension=false,bool corridorExtension=false)
        {
            var id=NavPhysicalTextPlan.Read(reader);long bytes=checked(LayoutBase+id.Bytes);
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);string name=id.ReadData(reader);int revision=reader.Input.ReadInt32();
                var value=new NavigationProfile(name,revision,reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),reader.Input.ReadDouble(),crowdExtension?reader.Input.ReadDouble():CrowdV1HeadingIntervals,crowdExtension?reader.Input.ReadDouble():CrowdV1SpeedBands,crowdExtension?reader.Input.ReadDouble():CrowdV1LookaheadSeconds,crowdExtension?reader.Input.ReadDouble():CrowdV1NeighborCap,corridorExtension?reader.Input.ReadInt32():0);if(corridorExtension&&value.GroupCorridorMaximumExcessPercent!=15)throw new ArgumentException("Missing corridor profile quality.");if(crowdExtension&&!corridorExtension&&value.HasCrowdV1Defaults)throw new ArgumentException("Redundant default crowd BODY extension.");var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.Facade=value;value.ownedProfile=storage;return storage;
            }
        }
    }
    public sealed partial class NavArrivalWorkingBinding : INavStorageFacade
    {
        private NavDeclarationStorage ownedBinding;
        NavOwnedStorage INavStorageFacade.StorageRoot=>ownedBinding;
        internal NavOwnedStorage StorageRoot=>ownedBinding;
        private static readonly long LayoutBase=Math.Max(192,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavArrivalWorkingBinding)));
        internal static NavArrivalWorkingBinding CreateOwned(long entity,ILayeredNavigationProvider provider,NavGraphProfile profile,INavStorageReservation reservation)
        {
            var root=(provider as INavStorageFacade)?.StorageRoot;if(entity<0||!(root is NavDeclarationStorage declaration)||!ReferenceEquals(declaration.Facade,provider)||!(provider is LayeredNavigationProvider)&&!(provider is PartitionNavigationProvider))throw new ArgumentException("Invalid concrete arrival working provider.");NavStorageAdmission.RequireOwned(reservation,root);NavMetadataOwnership.Require(profile,reservation);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(LayoutBase);var value=new NavArrivalWorkingBinding(entity,provider,profile);var storage=allocation.Register(new NavDeclarationStorage(LayoutBase));storage.First=NavPhysicalMacro.Acquire(storage,root);storage.Second=NavPhysicalMacro.Acquire(storage,profile.MetadataOwner);storage.Facade=value;value.ownedBinding=storage;storage.Retain();return value;}
        }
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            if(!ReferenceEquals(storage,ownedBinding)||!ReferenceEquals(storage.First,(Provider as INavStorageFacade)?.StorageRoot)||!ReferenceEquals(storage.Second,Profile.MetadataOwner)||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null||storage.StorageBytes!=NavStorageLayout.Charge(LayoutBase)||Entity<0)throw new ArgumentException("Physical arrival working binding owner/layout contradiction.");
            writer.Output.Write(Entity);NavPhysicalMacro.WriteEdge(writer,storage,storage.First);NavPhysicalMacro.WriteMetadata(writer,storage,Profile);
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            long entity=reader.Input.ReadInt64();var root=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);var profile=NavPhysicalMacro.ReadMetadata<NavGraphProfile>(reader,NavMetadataRole.GraphProfile);if(entity<0||!(root.Facade is ILayeredNavigationProvider provider))throw new ArgumentException("Invalid physical working provider/entity.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(LayoutBase);var value=new NavArrivalWorkingBinding(entity,provider,profile);var storage=allocation.Register(new NavDeclarationStorage(LayoutBase));storage.First=NavPhysicalMacro.Acquire(storage,root);storage.Second=NavPhysicalMacro.Acquire(storage,profile.MetadataOwner);storage.Facade=value;value.ownedBinding=storage;return storage;}
        }
    }
}

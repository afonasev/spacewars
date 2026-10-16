using System;

namespace Spacewars.Simulation
{
    public sealed partial class NavRouteSubscription
    {
        private static readonly long SubscriptionLayout=Math.Max(512,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavRouteSubscription)));
        private long SubscriptionWireBytes=>checked(93L+NavContract.LocationWireBytes(Origin)+NavContract.LocationWireBytes(AssignedEndpoint)+(HoldBinding==null?0:NavContract.StringWireBytes(HoldBinding)));
        internal static NavRouteSubscription CopyOwned(NavRouteSubscription source,INavStorageReservation reservation)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            source.Origin.RequireName(reservation);source.AssignedEndpoint.RequireName(reservation);if(source.MetadataOwner!=null)NavMetadataOwnership.Require(source,reservation);
            bool shared=ReferenceEquals(source.Origin.SurfaceId,source.AssignedEndpoint.SurfaceId);
            long bytes=checked(SubscriptionLayout+NavProviderCopy.TextBytes(source.Origin.SurfaceId)+(shared?0:NavProviderCopy.TextBytes(source.AssignedEndpoint.SurfaceId))+NavProviderCopy.TextBytes(source.HoldBinding)+NavProviderCopy.TextBytes(source.StableKey));
            using(var scratch=NavStorageScratch.Reserve(reservation,checked(8192+bytes+NavContract.DigestScratchBytes(source.SubscriptionWireBytes))))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));
                var origin=NavProviderCopy.Text(source.Origin.SurfaceId);var endpoint=shared?origin:NavProviderCopy.Text(source.AssignedEndpoint.SurfaceId);
                var value=new NavRouteSubscription(source.WorldGeneration,source.GroupId,source.RootOrderRevision,source.CommandSequence,source.IssuedTick,source.ActivationRevision,source.EntityId,source.Incarnation,source.MemberOrderRevision,source.MobilityRevision,source.RequestSequence,new NavLocation(source.Origin.Position,origin),new NavLocation(source.AssignedEndpoint.Position,endpoint),NavProviderCopy.Text(source.HoldBinding),source.Provenance);
                if(value.StableKey!=source.StableKey)throw new ArgumentException("Subscription copy changed semantic identity.");storage.Facade=value;value.BindStandaloneNames(storage);storage.Retain();return value;
            }
        }
        private void BindStandaloneNames(NavDeclarationStorage storage)
        {
            MetadataOwner=storage;Origin=new NavLocation(Origin.Position,new NavNameReference(storage,NavNameRole.SubscriptionOriginSurface));AssignedEndpoint=new NavLocation(AssignedEndpoint.Position,new NavNameReference(storage,NavNameRole.SubscriptionEndpointSurface));
        }
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            bool shared=ReferenceEquals(Origin.SurfaceId,AssignedEndpoint.SurfaceId);
            long bytes=checked(SubscriptionLayout+NavProviderCopy.TextBytes(Origin.SurfaceId)+(shared?0:NavProviderCopy.TextBytes(AssignedEndpoint.SurfaceId))+NavProviderCopy.TextBytes(HoldBinding)+NavProviderCopy.TextBytes(StableKey));
            if(!ReferenceEquals(MetadataOwner,storage)||storage.First!=null||storage.Second!=null||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null||storage.StorageBytes!=NavStorageLayout.Charge(bytes)||Origin.NameReference.Owner!=storage||Origin.NameReference.Role!=NavNameRole.SubscriptionOriginSurface||AssignedEndpoint.NameReference.Owner!=storage||AssignedEndpoint.NameReference.Role!=NavNameRole.SubscriptionEndpointSurface)throw new ArgumentException("Standalone subscription ownership/layout contradiction.");
            NavPhysicalTextPlan.WritePlan(writer,Origin.SurfaceId);writer.Output.Write(shared);if(!shared)NavPhysicalTextPlan.WritePlan(writer,AssignedEndpoint.SurfaceId);writer.Output.Write(HoldBinding!=null);if(HoldBinding!=null)writer.Output.Write(HoldBinding.Length);
            NavPhysicalTextPlan.WriteData(writer,Origin.SurfaceId);if(!shared)NavPhysicalTextPlan.WriteData(writer,AssignedEndpoint.SurfaceId);if(HoldBinding!=null)NavPhysicalTextPlan.WriteData(writer,HoldBinding);
            writer.Output.Write((int)Provenance);writer.Output.Write(WorldGeneration);writer.Output.Write(GroupId);writer.Output.Write(RootOrderRevision);writer.Output.Write(CommandSequence);writer.Output.Write(IssuedTick);writer.Output.Write(ActivationRevision);writer.Output.Write(EntityId);writer.Output.Write(Incarnation);writer.Output.Write(MemberOrderRevision);writer.Output.Write(MobilityRevision);writer.Output.Write(RequestSequence);
            NavContract.Point(writer.Output,Origin.Position);NavContract.Point(writer.Output,AssignedEndpoint.Position);NavPhysicalTextPlan.WriteData(writer,NavContract.Sha256(StableKey));
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var originPlan=NavPhysicalTextPlan.Read(reader);bool shared=NavPhysicalMacro.ReadBoolean(reader);var endpointPlan=shared?default:NavPhysicalTextPlan.Read(reader);bool hasHold=NavPhysicalMacro.ReadBoolean(reader);var holdPlan=hasHold?NavPhysicalTextPlan.Read(reader):default;
            // Reserve every destination string and facade before ReadData or
            // constructor digest. No retained public checkpoint is borrowed.
            long bytes=checked(SubscriptionLayout+originPlan.Bytes+(shared?0:endpointPlan.Bytes)+(hasHold?holdPlan.Bytes:0)+160);
            long wire=checked(93+42L+3L*originPlan.Length+3L*(shared?originPlan.Length:endpointPlan.Length)+(hasHold?5L+holdPlan.Length*3L:0));
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));
                using(var scratch=NavStorageScratch.Reserve(reader.Reservation,checked(8192+NavContract.DigestScratchBytes(wire)))){
                    string origin=originPlan.ReadData(reader),endpoint=shared?origin:endpointPlan.ReadData(reader),hold=hasHold?holdPlan.ReadRawData(reader):null;
                    var provenance=(NavRouteProvenance)reader.Input.ReadInt32();long world=reader.Input.ReadInt64(),group=reader.Input.ReadInt64(),root=reader.Input.ReadInt64(),command=reader.Input.ReadInt64(),issued=reader.Input.ReadInt64(),activation=reader.Input.ReadInt64(),entity=reader.Input.ReadInt64(),incarnation=reader.Input.ReadInt64(),member=reader.Input.ReadInt64(),mobility=reader.Input.ReadInt64(),request=reader.Input.ReadInt64();
                    var from=new NavPoint(reader.Input.ReadDouble(),reader.Input.ReadDouble());var to=new NavPoint(reader.Input.ReadDouble(),reader.Input.ReadDouble());
                    var value=new NavRouteSubscription(world,group,root,command,issued,activation,entity,incarnation,member,mobility,request,new NavLocation(from,origin),new NavLocation(to,endpoint),hold,provenance);
                    for(int n=0;n<64;n++)if(reader.Input.ReadUInt16()!=value.StableKey[n])throw new ArgumentException("Standalone subscription recomputed digest mismatch.");storage.Facade=value;value.BindStandaloneNames(storage);return storage;
                }
            }
        }
    }
}

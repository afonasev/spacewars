using System;

namespace Spacewars.Simulation
{
    public sealed partial class NavScheduledRequest
    {
        internal static readonly long ExternalLayoutBase=Math.Max(1024,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavScheduledRequest))+NavStorageLayout.ObjectBytesBound(typeof(NavRouteSubscription))+NavStorageLayout.ObjectBytesBound(typeof(NavScheduledIdentity))+4*160);
        private static long ExternalDigestScratch(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavRouteSubscription subscription)
        {
            long graphWire=checked(NavContract.StringWireBytes("layered-graph-v1")+NavContract.StringWireBytes(provider.Binding)+NavContract.StringWireBytes(clearance.Id)+4L+NavContract.StringWireBytes(clearance.MobilityClass)+12L+NavContract.StringWireBytes(profile.ExactProfileBinding)+NavContract.StringWireBytes(profile.CostId)+12L);
            long subWire=checked(4L+88L+NavContract.LocationWireBytes(subscription.Origin)+NavContract.LocationWireBytes(subscription.AssignedEndpoint)+1L+(subscription.HoldBinding==null?0:NavContract.StringWireBytes(subscription.HoldBinding)));
            return checked(NavContract.DigestScratchBytes(graphWire)+NavContract.DigestScratchBytes(subWire)+NavContract.DigestScratchBytes(77)+NavContract.DigestScratchBytes(135));
        }
        internal static NavScheduledRequest CreateOwned(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,NavTerminalRegion region,NavRouteSubscription source,int maximum,INavStorageReservation reservation,bool singleton=false)
        {
            if(provider==null||source==null||maximum<1||maximum>1048576||!singleton&&region==null)throw new ArgumentException("Invalid owned recipe input.");
            var providerRoot=(provider as INavStorageFacade)?.StorageRoot;if(!(providerRoot is NavDeclarationStorage declaration)||!ReferenceEquals(declaration.Facade,provider)||!(provider is LayeredNavigationProvider)&&!(provider is PartitionNavigationProvider))throw new ArgumentException("Owned recipe requires its exact concrete provider facade.");NavStorageAdmission.RequireOwned(reservation,providerRoot);NavMetadataOwnership.RequireGraph(clearance,profile,limits,reservation);if(region!=null)NavStorageAdmission.RequireOwned(reservation,region.StorageRoot);
            source.Origin.RequireName(reservation);source.AssignedEndpoint.RequireName(reservation);if(source.MetadataOwner!=null)NavMetadataOwnership.Require(source,reservation);
            var origin=Location(source.Origin,provider);var endpoint=Location(source.AssignedEndpoint,provider);
            long bytes=checked(ExternalLayoutBase+NavProviderCopy.TextBytes(source.HoldBinding));
            using(NavStorageScratch.Reserve(reservation,8192+ExternalDigestScratch(provider,clearance,profile,source)))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));
                storage.First=NavPhysicalMacro.Acquire(storage,providerRoot);storage.Second=NavPhysicalMacro.Acquire(storage,region?.StorageRoot);storage.Third=NavPhysicalMacro.Acquire(storage,clearance.MetadataOwner);storage.Fourth=NavPhysicalMacro.Acquire(storage,profile.MetadataOwner);storage.Fifth=NavPhysicalMacro.Acquire(storage,limits.MetadataOwner);
                var subscription=new NavRouteSubscription(source.WorldGeneration,source.GroupId,source.RootOrderRevision,source.CommandSequence,source.IssuedTick,source.ActivationRevision,source.EntityId,source.Incarnation,source.MemberOrderRevision,source.MobilityRevision,source.RequestSequence,origin,endpoint,NavProviderCopy.Text(source.HoldBinding),source.Provenance);
                var result=new NavScheduledRequest(provider,clearance,profile,limits,region,subscription,maximum,singleton){ownedRecipe=storage};storage.Facade=result;subscription.MetadataOwner=storage;storage.Retain();return result;
            }
        }
        private void WriteExternalPhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            if(!ReferenceEquals(storage,ownedRecipe)||!ReferenceEquals(storage.First,(Provider as INavStorageFacade)?.StorageRoot)||!ReferenceEquals(storage.Second,Region?.StorageRoot)||!ReferenceEquals(storage.Third,Clearance.MetadataOwner)||!ReferenceEquals(storage.Fourth,Profile.MetadataOwner)||!ReferenceEquals(storage.Fifth,Limits.MetadataOwner)||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null||!ReferenceEquals(Subscription.MetadataOwner,storage)||storage.StorageBytes!=NavStorageLayout.Charge(ExternalLayoutBase+NavProviderCopy.TextBytes(Subscription.HoldBinding)))throw new ArgumentException("External recipe owner/layout contradiction.");
            RequireProviderLocation(Provider,Subscription.Origin);RequireProviderLocation(Provider,Subscription.AssignedEndpoint);
            writer.Output.Write(Subscription.HoldBinding!=null);if(Subscription.HoldBinding!=null)writer.Output.Write(Subscription.HoldBinding.Length);
            NavPhysicalMacro.WriteEdge(writer,storage,storage.First);NavPhysicalMacro.WriteEdge(writer,storage,storage.Second,true);NavPhysicalMacro.WriteMetadata(writer,storage,Clearance);NavPhysicalMacro.WriteMetadata(writer,storage,Profile);NavPhysicalMacro.WriteMetadata(writer,storage,Limits);
            if(Subscription.HoldBinding!=null)NavPhysicalTextPlan.WriteData(writer,Subscription.HoldBinding);writer.Output.Write((int)Subscription.Provenance);
            foreach(long value in new[]{Subscription.WorldGeneration,Subscription.GroupId,Subscription.RootOrderRevision,Subscription.CommandSequence,Subscription.IssuedTick,Subscription.ActivationRevision,Subscription.EntityId,Subscription.Incarnation,Subscription.MemberOrderRevision,Subscription.MobilityRevision,Subscription.RequestSequence})writer.Output.Write(value);
            NavPhysicalNamedValue.WriteLocation(writer,Subscription.Origin);NavPhysicalNamedValue.WriteLocation(writer,Subscription.AssignedEndpoint);writer.Output.Write(Singleton);writer.Output.Write(MaxLabels);
            NavPhysicalTextPlan.WriteData(writer,GraphKey);NavPhysicalTextPlan.WriteData(writer,CompileKey);NavPhysicalTextPlan.WriteData(writer,ArtifactKey);NavPhysicalTextPlan.WriteData(writer,Subscription.StableKey);
        }
        private static NavDeclarationStorage ReadExternalPhysicalBody(NavPhysicalBodyReader reader)
        {
            bool hold=Boolean(reader);var holding=hold?NavPhysicalTextPlan.Read(reader):default;
            var providerRoot=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);var regionRoot=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader,true);if(!(providerRoot.Facade is ILayeredNavigationProvider provider)||regionRoot!=null&&!(regionRoot.Facade is NavTerminalRegion))throw new ArgumentException("Wrong external recipe provider/region kind.");
            var clearance=NavPhysicalMacro.ReadMetadata<NavClearanceProfile>(reader,NavMetadataRole.Clearance);var profile=NavPhysicalMacro.ReadMetadata<NavGraphProfile>(reader,NavMetadataRole.GraphProfile);var limits=NavPhysicalMacro.ReadMetadata<NavGraphCompileLimits>(reader,NavMetadataRole.CompileLimits);
            long bytes=checked(ExternalLayoutBase+(hold?holding.Bytes:0));
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);
                using(NavStorageScratch.Reserve(reader.Reservation,8192)){
                    string held=hold?holding.ReadRawData(reader):null;var provenance=(NavRouteProvenance)reader.Input.ReadInt32();long world=reader.Input.ReadInt64(),group=reader.Input.ReadInt64(),root=reader.Input.ReadInt64(),command=reader.Input.ReadInt64(),issued=reader.Input.ReadInt64(),activation=reader.Input.ReadInt64(),entity=reader.Input.ReadInt64(),incarnation=reader.Input.ReadInt64(),memberOrder=reader.Input.ReadInt64(),mobility=reader.Input.ReadInt64(),request=reader.Input.ReadInt64();var origin=NavPhysicalNamedValue.ReadLocation(reader);var endpoint=NavPhysicalNamedValue.ReadLocation(reader);RequireProviderLocation(provider,origin);RequireProviderLocation(provider,endpoint);bool singleton=Boolean(reader);int max=reader.Count(1048576);var region=regionRoot?.Facade as NavTerminalRegion;if(max==0||!singleton&&region==null)throw new ArgumentException("Invalid external recipe region/labels.");
                    long graphWire=checked(NavContract.StringWireBytes("layered-graph-v1")+NavContract.StringWireBytes(provider.Binding)+NavContract.StringWireBytes(clearance.Id)+4L+NavContract.StringWireBytes(clearance.MobilityClass)+12L+NavContract.StringWireBytes(profile.ExactProfileBinding)+NavContract.StringWireBytes(profile.CostId)+12L);long subWire=checked(4L+88L+NavContract.LocationWireBytes(origin)+NavContract.LocationWireBytes(endpoint)+1L+(held==null?0:NavContract.StringWireBytes(held)));
                    using(NavStorageScratch.Reserve(reader.Reservation,checked(NavContract.DigestScratchBytes(graphWire)+NavContract.DigestScratchBytes(subWire)+NavContract.DigestScratchBytes(77)+NavContract.DigestScratchBytes(135)))){
                        var subscription=new NavRouteSubscription(world,group,root,command,issued,activation,entity,incarnation,memberOrder,mobility,request,origin,endpoint,held,provenance);var value=new NavScheduledRequest(provider,clearance,profile,limits,region,subscription,max,singleton);string graph=Digest(reader),compile=Digest(reader),artifact=Digest(reader),subKey=Digest(reader);if(value.GraphKey!=graph||value.CompileKey!=compile||value.ArtifactKey!=artifact||subscription.StableKey!=subKey)throw new ArgumentException("External recipe recomputed digest mismatch.");
                        var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.First=NavPhysicalMacro.Acquire(storage,providerRoot);storage.Second=NavPhysicalMacro.Acquire(storage,regionRoot);storage.Third=NavPhysicalMacro.Acquire(storage,clearance.MetadataOwner);storage.Fourth=NavPhysicalMacro.Acquire(storage,profile.MetadataOwner);storage.Fifth=NavPhysicalMacro.Acquire(storage,limits.MetadataOwner);storage.Facade=value;value.ownedRecipe=storage;subscription.MetadataOwner=storage;return storage;
                    }
                }
            }
        }
    }
}

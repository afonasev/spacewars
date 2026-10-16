using System;
using System.Collections.ObjectModel;

namespace Spacewars.Simulation
{
    public sealed partial class NavTerminalRegion
    {
        internal static readonly long LayoutBase=Math.Max(416,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavTerminalRegion))+NavStorageLayout.ObjectBytesBound(typeof(ReadOnlyCollection<NavLocation>))+32+160);
        internal long LayoutBytes(){long bytes=LayoutBase+64L*Candidates.Count+NavProviderCopy.TextBytes(Id)+NavProviderCopy.TextBytes(Requested.SurfaceId);foreach(var location in Candidates)bytes=checked(bytes+NavProviderCopy.TextBytes(location.SurfaceId));return bytes;}
        private void BindOwnedLocations()
        {
            Requested=new NavLocation(Requested.Position,new NavNameReference(ownedRegion,NavNameRole.RegionRequestedSurface));
            for(int n=0;n<candidatesArray.Length;n++)candidatesArray[n]=new NavLocation(candidatesArray[n].Position,new NavNameReference(ownedRegion,NavNameRole.RegionCandidateSurface,n));
        }
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            if(!ReferenceEquals(storage,ownedRegion)||storage.First!=null||storage.Second!=null||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null)throw new ArgumentException("Invalid physical terminal aggregate.");
            if(storage.StorageBytes!=NavStorageLayout.Charge(LayoutBytes())||Requested.NameReference.Owner!=storage||Requested.NameReference.Role!=NavNameRole.RegionRequestedSurface||Requested.NameReference.Index!=-1)throw new ArgumentException("Physical terminal layout/role contradiction.");for(int n=0;n<Candidates.Count;n++){var name=Candidates[n].NameReference;if(name.Owner!=storage||name.Role!=NavNameRole.RegionCandidateSurface||name.Index!=n||!ReferenceEquals(name.Resolve(),Candidates[n].SurfaceId))throw new ArgumentException("Physical candidate name role contradiction.");}
            NavPhysicalTextPlan.WritePlan(writer,Id);NavPhysicalTextPlan.WritePlan(writer,StableKey);NavPhysicalTextPlan.WritePlan(writer,Requested.SurfaceId);writer.Output.Write(Candidates.Count);for(int n=0;n<Candidates.Count;n++)NavPhysicalTextPlan.WritePlan(writer,Candidates[n].SurfaceId);
            NavPhysicalTextPlan.WriteData(writer,Id);NavPhysicalTextPlan.WriteData(writer,StableKey);NavPhysicalTextPlan.WriteData(writer,Requested.SurfaceId);for(int n=0;n<Candidates.Count;n++)NavPhysicalTextPlan.WriteData(writer,Candidates[n].SurfaceId);writer.Output.Write(Revision);NavContract.Point(writer.Output,Requested.Position);foreach(var location in Candidates)NavContract.Point(writer.Output,location.Position);
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            using(var scratch=NavStorageScratch.Reserve(reader.Reservation,8192+128*64)){
                var idPlan=NavPhysicalTextPlan.Read(reader);var digestPlan=NavPhysicalTextPlan.Read(reader);if(digestPlan.Length!=64)throw new ArgumentException("Invalid terminal digest length.");var requestedPlan=NavPhysicalTextPlan.Read(reader);int count=reader.Count(64);if(count==0)throw new ArgumentException("Empty physical terminal region.");var plans=new NavPhysicalTextPlan[count];long bytes=LayoutBase+64L*count+idPlan.Bytes+requestedPlan.Bytes;for(int n=0;n<count;n++){plans[n]=NavPhysicalTextPlan.Read(reader);bytes=checked(bytes+plans[n].Bytes);}
                long wire=5+3L*idPlan.Length+24+5+3L*requestedPlan.Length;foreach(var plan in plans)wire=checked(wire+21+3L*plan.Length);
                using(var digestScratch=NavStorageScratch.Reserve(reader.Reservation,NavContract.DigestScratchBytes(wire)))
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                    allocation.Reserve(bytes);string id=idPlan.ReadData(reader),expected=NavContract.Sha256(digestPlan.ReadData(reader)),requestedName=requestedPlan.ReadData(reader);var names=new string[count];for(int n=0;n<count;n++)names[n]=plans[n].ReadData(reader);int revision=NavContract.Revision(reader.Input.ReadInt32());var requested=new NavLocation(NavPhysicalGeometryValue.Point(reader),requestedName);var candidates=new NavLocation[count];for(int n=0;n<count;n++)candidates[n]=new NavLocation(NavPhysicalGeometryValue.Point(reader),names[n]);
                    var storage=allocation.Register(new NavDeclarationStorage(bytes));var value=new NavTerminalRegion(id,revision,requested,candidates){ownedRegion=storage};if(value.StableKey!=expected)throw new ArgumentException("Physical terminal binding mismatch.");storage.Facade=value;value.BindOwnedLocations();return storage;
                }
            }
        }
    }
    public sealed partial class NavScheduledRequest
    {
        internal static readonly long LayoutBase=Math.Max(1408,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavScheduledRequest))+NavStorageLayout.ObjectBytesBound(typeof(NavClearanceProfile))+NavStorageLayout.ObjectBytesBound(typeof(NavGraphProfile))+NavStorageLayout.ObjectBytesBound(typeof(NavGraphCompileLimits))+NavStorageLayout.ObjectBytesBound(typeof(NavRouteSubscription))+NavStorageLayout.ObjectBytesBound(typeof(NavScheduledIdentity))+4*160);
        private static void RequireProviderLocation(ILayeredNavigationProvider provider,NavLocation location){foreach(var surface in provider.Surfaces)if(ReferenceEquals(surface.Id,location.SurfaceId)&&ReferenceEquals(surface.StorageRoot,location.NameReference.Owner)&&location.NameReference.Role==NavNameRole.SurfaceId&&location.NameReference.Index==-1)return;throw new ArgumentException("Recipe location must use its provider exact surface role.");}
        private long LayoutBytes()=>LayoutBase+NavProviderCopy.TextBytes(Clearance.Id)+NavProviderCopy.TextBytes(Clearance.MobilityClass)+NavProviderCopy.TextBytes(Profile.ExactProfileBinding)+NavProviderCopy.TextBytes(Profile.CostId)+NavProviderCopy.TextBytes(Subscription.HoldBinding);
        private static bool Boolean(NavPhysicalBodyReader reader){byte value=reader.Input.ReadByte();if(value>1)throw new ArgumentException("Invalid recipe boolean.");return value==1;}
        private static string Digest(NavPhysicalBodyReader reader){var chars=new char[64];for(int n=0;n<64;n++)chars[n]=(char)reader.Input.ReadUInt16();return NavContract.Sha256(new string(chars));}
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            bool external=storage.Third!=null;writer.Output.Write(external);if(external){WriteExternalPhysicalBody(writer,storage);return;}

            if(!ReferenceEquals(storage,ownedRecipe)||storage.First!=(Provider as INavStorageFacade)?.StorageRoot||storage.Second!=Region?.StorageRoot||storage.Third!=null||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null||storage.StorageBytes!=NavStorageLayout.Charge(LayoutBytes()))throw new ArgumentException("Recipe physical ownership/layout contradiction.");
            RequireProviderLocation(Provider,Subscription.Origin);RequireProviderLocation(Provider,Subscription.AssignedEndpoint);
            NavMetadataOwnership.Resolve(storage,NavMetadataRole.Clearance);NavMetadataOwnership.Resolve(storage,NavMetadataRole.GraphProfile);NavMetadataOwnership.Resolve(storage,NavMetadataRole.Subscription);NavMetadataOwnership.Resolve(storage,NavMetadataRole.CompileLimits);
            NavPhysicalTextPlan.WritePlan(writer,Clearance.Id);NavPhysicalTextPlan.WritePlan(writer,Clearance.MobilityClass);NavPhysicalTextPlan.WritePlan(writer,Profile.ExactProfileBinding);NavPhysicalTextPlan.WritePlan(writer,Profile.CostId);writer.Output.Write(Subscription.HoldBinding!=null);if(Subscription.HoldBinding!=null)writer.Output.Write(Subscription.HoldBinding.Length);
            writer.Edge(storage.First);writer.Edge(storage.Second);
            NavPhysicalTextPlan.WriteData(writer,Clearance.Id);NavPhysicalTextPlan.WriteData(writer,Clearance.MobilityClass);NavPhysicalTextPlan.WriteData(writer,Profile.ExactProfileBinding);NavPhysicalTextPlan.WriteData(writer,Profile.CostId);if(Subscription.HoldBinding!=null)NavPhysicalTextPlan.WriteData(writer,Subscription.HoldBinding);
            writer.Output.Write(Clearance.Revision);writer.Output.Write(Clearance.MobilityRevision);writer.Output.Write(Clearance.Radius);writer.Output.Write(Profile.CostRevision);writer.Output.Write(Profile.GridCell);writer.Output.Write(Limits.MaxSamples);writer.Output.Write(Limits.MaxNodes);writer.Output.Write(Limits.MaxArcs);
            writer.Output.Write((int)Subscription.Provenance);foreach(long value in new[]{Subscription.WorldGeneration,Subscription.GroupId,Subscription.RootOrderRevision,Subscription.CommandSequence,Subscription.IssuedTick,Subscription.ActivationRevision,Subscription.EntityId,Subscription.Incarnation,Subscription.MemberOrderRevision,Subscription.MobilityRevision,Subscription.RequestSequence})writer.Output.Write(value);
            NavPhysicalNamedValue.WriteLocation(writer,Subscription.Origin);NavPhysicalNamedValue.WriteLocation(writer,Subscription.AssignedEndpoint);writer.Output.Write(Singleton);writer.Output.Write(MaxLabels);
            NavPhysicalTextPlan.WriteData(writer,NavContract.Sha256(GraphKey));NavPhysicalTextPlan.WriteData(writer,NavContract.Sha256(CompileKey));NavPhysicalTextPlan.WriteData(writer,NavContract.Sha256(ArtifactKey));NavPhysicalTextPlan.WriteData(writer,NavContract.Sha256(Subscription.StableKey));
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            if(Boolean(reader))return ReadExternalPhysicalBody(reader);

            using(var scratch=NavStorageScratch.Reserve(reader.Reservation,8192)){
                var a=NavPhysicalTextPlan.Read(reader);var b=NavPhysicalTextPlan.Read(reader);var c=NavPhysicalTextPlan.Read(reader);var d=NavPhysicalTextPlan.Read(reader);bool hold=Boolean(reader);var h=hold?NavPhysicalTextPlan.Read(reader):default;long bytes=LayoutBase+a.Bytes+b.Bytes+c.Bytes+d.Bytes+(hold?h.Bytes:0);
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                    allocation.Reserve(bytes);var providerRoot=reader.Edge<NavDeclarationStorage>();var regionRoot=reader.Edge<NavDeclarationStorage>();var provider=providerRoot?.Facade as ILayeredNavigationProvider;var region=regionRoot?.Facade as NavTerminalRegion;if(provider==null||regionRoot!=null&&region==null)throw new ArgumentException("Wrong recipe provider/region kind.");
                    string id=a.ReadData(reader),mobility=b.ReadData(reader),exact=c.ReadData(reader),cost=d.ReadData(reader),holding=hold?h.ReadRawData(reader):null;
                    var clearance=new NavClearanceProfile(id,reader.Input.ReadInt32(),mobility,reader.Input.ReadInt32(),reader.Input.ReadDouble());var profile=new NavGraphProfile(exact,cost,reader.Input.ReadInt32(),reader.Input.ReadDouble());var limits=new NavGraphCompileLimits(reader.Input.ReadInt32(),reader.Input.ReadInt32(),reader.Input.ReadInt32());
                    var provenance=(NavRouteProvenance)reader.Input.ReadInt32();long world=reader.Input.ReadInt64(),group=reader.Input.ReadInt64(),rootOrder=reader.Input.ReadInt64(),command=reader.Input.ReadInt64(),issued=reader.Input.ReadInt64(),activation=reader.Input.ReadInt64(),entity=reader.Input.ReadInt64(),incarnation=reader.Input.ReadInt64(),memberOrder=reader.Input.ReadInt64(),mobilityRevision=reader.Input.ReadInt64(),request=reader.Input.ReadInt64();var origin=NavPhysicalNamedValue.ReadLocation(reader);var endpoint=NavPhysicalNamedValue.ReadLocation(reader);RequireProviderLocation(provider,origin);RequireProviderLocation(provider,endpoint);bool singleton=Boolean(reader);int max=reader.Count(1048576);if(max==0||!singleton&&region==null)throw new ArgumentException("Invalid recipe label/region capacity.");
                    long graphWire=NavContract.StringWireBytes("layered-graph-v1")+NavContract.StringWireBytes(provider.Binding)+NavContract.StringWireBytes(id)+4+NavContract.StringWireBytes(mobility)+12+NavContract.StringWireBytes(exact)+NavContract.StringWireBytes(cost)+12;
                    long subscriptionWire=4+88+NavContract.LocationWireBytes(origin)+NavContract.LocationWireBytes(endpoint)+1+(holding==null?0:NavContract.StringWireBytes(holding));long digestBytes=NavContract.DigestScratchBytes(graphWire)+NavContract.DigestScratchBytes(subscriptionWire)+NavContract.DigestScratchBytes(77)+NavContract.DigestScratchBytes(135);
                    using(var digestScratch=NavStorageScratch.Reserve(reader.Reservation,digestBytes)){
                        var subscription=new NavRouteSubscription(world,group,rootOrder,command,issued,activation,entity,incarnation,memberOrder,mobilityRevision,request,origin,endpoint,holding,provenance);
                        var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.RequireEdge(providerRoot);providerRoot.Retain();storage.First=providerRoot;if(regionRoot!=null){storage.RequireEdge(regionRoot);regionRoot.Retain();storage.Second=regionRoot;}
                        var value=new NavScheduledRequest(provider,clearance,profile,limits,region,subscription,max,singleton){ownedRecipe=storage};string graph=Digest(reader),compile=Digest(reader),artifact=Digest(reader),subscriptionKey=Digest(reader);if(value.GraphKey!=graph||value.CompileKey!=compile||value.ArtifactKey!=artifact||subscription.StableKey!=subscriptionKey)throw new ArgumentException("Recipe recomputed digest mismatch.");
                        storage.Facade=value;clearance.MetadataOwner=storage;profile.MetadataOwner=storage;limits.MetadataOwner=storage;subscription.MetadataOwner=storage;return storage;
                    }
                }
            }
        }
    }

}

using System;

namespace Spacewars.Simulation
{
    public sealed partial class NavGraphBinding : INavStorageFacade
    {
        private NavDeclarationStorage ownedBinding;
        internal NavOwnedStorage StorageRoot=>ownedBinding;
        NavOwnedStorage INavStorageFacade.StorageRoot=>ownedBinding;
        private NavGraphBinding(string providerBinding,NavClearanceProfile clearance,NavGraphProfile profile,string stableKey)
        {ProviderBinding=providerBinding;Clearance=clearance;Profile=profile;StableKey=stableKey;}
        // Returned binding has a scope birth reference, not an independent
        // owner reference: its graph must retain it before scope resolution.
        internal static NavGraphBinding CreatePhysical(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,INavStorageReservation reservation)
        {
            var providerRoot=(provider as INavStorageFacade)?.StorageRoot;NavStorageAdmission.RequireOwned(reservation,providerRoot);
            var c=NavMetadataOwnership.Require(clearance,reservation);var p=NavMetadataOwnership.Require(profile,reservation);
            long wire=checked(NavContract.StringWireBytes("layered-graph-v1")+NavContract.StringWireBytes(provider.Binding)+NavContract.StringWireBytes(clearance.Id)+NavContract.StringWireBytes(clearance.MobilityClass)+16L+NavContract.StringWireBytes(profile.ExactProfileBinding)+NavContract.StringWireBytes(profile.CostId)+12L);
            using(var scratch=NavStorageScratch.Reserve(reservation,NavContract.DigestScratchBytes(wire)))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(416);var storage=allocation.Register(new NavDeclarationStorage(416));
                var value=new NavGraphBinding(provider.Binding,clearance,profile){ownedBinding=storage};storage.Facade=value;
                providerRoot.Retain();storage.First=providerRoot;c.Retain();storage.Second=c;if(!ReferenceEquals(c,p)){p.Retain();storage.Third=p;}return value;
            }
        }
        internal void RequireAssociation(ILayeredNavigationProvider provider,object reservation)
        {
            var root=(provider as INavStorageFacade)?.StorageRoot;NavStorageAdmission.RequireLedger(reservation,root);NavStorageAdmission.RequireLedger(reservation,ownedBinding);RequireAssociation(provider);
        }
        internal void RequireAssociation(ILayeredNavigationProvider provider)
        {
            var root=(provider as INavStorageFacade)?.StorageRoot;
            if(root==null||ownedBinding==null||ownedBinding.First!=root||!ReferenceEquals(ProviderBinding,provider.Binding)||ownedBinding.Second!=Clearance.MetadataOwner||(ownedBinding.Third??ownedBinding.Second)!=Profile.MetadataOwner||!ReferenceEquals(Clearance,NavMetadataOwnership.Resolve(Clearance.MetadataOwner,NavMetadataRole.Clearance))||!ReferenceEquals(Profile,NavMetadataOwnership.Resolve(Profile.MetadataOwner,NavMetadataRole.GraphProfile)))throw new ArgumentException("Graph binding must own this exact provider and metadata fields.");
        }
        internal void WritePhysicalBody(NavPhysicalBodyWriter writer,NavDeclarationStorage storage)
        {
            var provider=(storage.First as NavDeclarationStorage)?.Facade as ILayeredNavigationProvider;
            if(provider==null||!ReferenceEquals(provider.Binding,ProviderBinding)||storage.StorageBytes!=NavStorageLayout.Charge(256+NavProviderCopy.TextBytes(StableKey))||!ReferenceEquals(Clearance.MetadataOwner,storage.Second)||!ReferenceEquals(Profile.MetadataOwner,storage.Third??storage.Second)||storage.Fourth!=null||storage.Fifth!=null||storage.Sixth!=null||storage.Seventh!=null||storage.Eighth!=null)throw new ArgumentException("Physical graph binding ownership contradiction.");
            NavContract.Sha256(StableKey);if(!ReferenceEquals(Clearance,NavMetadataOwnership.Resolve(Clearance.MetadataOwner,NavMetadataRole.Clearance))||!ReferenceEquals(Profile,NavMetadataOwnership.Resolve(Profile.MetadataOwner,NavMetadataRole.GraphProfile)))throw new ArgumentException("Graph binding metadata field identity contradiction.");
            NavPhysicalTextPlan.WritePlan(writer,StableKey);NavPhysicalTextPlan.WriteData(writer,StableKey);writer.Edge(storage.First);
            new NavMetadataReference(Clearance.MetadataOwner,NavMetadataRole.Clearance).Write(writer);new NavMetadataReference(Profile.MetadataOwner,NavMetadataRole.GraphProfile).Write(writer);
        }
        internal static NavDeclarationStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var keyPlan=NavPhysicalTextPlan.Read(reader);if(keyPlan.Length!=64)throw new ArgumentException("Invalid graph binding digest length.");long bytes=256+keyPlan.Bytes;
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(bytes);string key=NavContract.Sha256(keyPlan.ReadData(reader));var providerRoot=reader.Edge<NavDeclarationStorage>();var provider=providerRoot?.Facade as ILayeredNavigationProvider;
                var cr=NavMetadataReference.Read(reader);var pr=NavMetadataReference.Read(reader);
                if(provider==null||cr.Role!=NavMetadataRole.Clearance||pr.Role!=NavMetadataRole.GraphProfile)throw new ArgumentException("Wrong graph binding owner/role.");
                var clearance=(NavClearanceProfile)cr.Value;var profile=(NavGraphProfile)pr.Value;
                using(var scratch=NavStorageScratch.Reserve(reader.Reservation,8192))NavPhysicalDigestStream.Require(w=>{w.Write("layered-graph-v1");w.Write(provider.Binding);clearance.Write(w);profile.Write(w);},key);
                var storage=allocation.Register(new NavDeclarationStorage(bytes));storage.RequireEdge(providerRoot);storage.RequireEdge(cr.Owner);storage.RequireEdge(pr.Owner);
                providerRoot.Retain();storage.First=providerRoot;cr.Owner.Retain();storage.Second=cr.Owner;if(!ReferenceEquals(cr.Owner,pr.Owner)){pr.Owner.Retain();storage.Third=pr.Owner;}
                storage.Facade=new NavGraphBinding(provider.Binding,clearance,profile,key){ownedBinding=storage};return storage;
            }
        }
    }
}

using System;

namespace Spacewars.Simulation
{
    public sealed partial class NavScheduledRequest : INavStorageFacade
    {
        private NavDeclarationStorage ownedRecipe;
        NavOwnedStorage INavStorageFacade.StorageRoot=>ownedRecipe;
        internal NavOwnedStorage StorageRoot=>ownedRecipe;
        // Public immutable recipes are inputs. The destination keeps its own
        // declarations/strings and one admitted provider edge; no public array
        // or unregistered provider can be adopted into a prepared queue.
        internal static NavScheduledRequest CopyOwned(NavScheduledRequest source,ILayeredNavigationProvider provider,INavStorageReservation reservation,NavTerminalRegion region=null)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            NavStorageAdmission.RequireOwned(reservation,(provider as INavStorageFacade)?.StorageRoot);source.Subscription.Origin.RequireName(reservation);source.Subscription.AssignedEndpoint.RequireName(reservation);
            if(source.Clearance.MetadataOwner!=null)NavMetadataOwnership.Require(source.Clearance,reservation);
            if(source.Profile.MetadataOwner!=null)NavMetadataOwnership.Require(source.Profile,reservation);
            if(source.Subscription.MetadataOwner!=null)NavMetadataOwnership.Require(source.Subscription,reservation);
            if(source.Limits.MetadataOwner!=null)NavMetadataOwnership.Require(source.Limits,reservation);
            if(provider.Binding!=source.Provider.Binding)throw new ArgumentException("Owned recipe provider binding changed.");
            if(region!=null){NavStorageAdmission.RequireOwned(reservation,region.StorageRoot);if(source.Region==null||region.StableKey!=source.Region.StableKey)throw new ArgumentException("Owned terminal recipe changed.");}
            if(region==null&&source.Region?.StorageRoot!=null){NavStorageAdmission.RequireOwned(reservation,source.Region.StorageRoot);region=source.Region;}
            int count=source.Region?.Candidates.Count??0;
            long bytes=checked(LayoutBase+NavProviderCopy.TextBytes(source.Clearance.Id)+NavProviderCopy.TextBytes(source.Clearance.MobilityClass)+
                NavProviderCopy.TextBytes(source.Profile.ExactProfileBinding)+NavProviderCopy.TextBytes(source.Profile.CostId)+
                NavProviderCopy.TextBytes(source.Subscription.HoldBinding));
            var sourceClearance=source.Clearance;var sourceProfile=source.Profile;var sourceSubscription=source.Subscription;
            long graphWire=checked(NavContract.StringWireBytes("layered-graph-v1")+NavContract.StringWireBytes(provider.Binding)+NavContract.StringWireBytes(sourceClearance.Id)+4L+NavContract.StringWireBytes(sourceClearance.MobilityClass)+12L+
                NavContract.StringWireBytes(sourceProfile.ExactProfileBinding)+NavContract.StringWireBytes(sourceProfile.CostId)+12L);
            long subscriptionWire=checked(4L+88L+NavContract.LocationWireBytes(sourceSubscription.Origin)+NavContract.LocationWireBytes(sourceSubscription.AssignedEndpoint)+1L+
                (sourceSubscription.HoldBinding==null?0:NavContract.StringWireBytes(sourceSubscription.HoldBinding)));
            long digestScratch=checked(NavContract.DigestScratchBytes(graphWire)+NavContract.DigestScratchBytes(subscriptionWire)+NavContract.DigestScratchBytes(77)+NavContract.DigestScratchBytes(135+(source.Query?192:0)));
            using(var scratch=NavStorageScratch.Reserve(reservation,8192L+128L*count+digestScratch))
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(bytes);var storage=allocation.Register(new NavDeclarationStorage(bytes));
                var providerRoot=((INavStorageFacade)provider).StorageRoot;providerRoot.Retain();storage.First=providerRoot;
                var c=source.Clearance;var clearance=new NavClearanceProfile(NavProviderCopy.Text(c.Id),c.Revision,NavProviderCopy.Text(c.MobilityClass),c.MobilityRevision,c.Radius);
                var p=source.Profile;var profile=new NavGraphProfile(NavProviderCopy.Text(p.ExactProfileBinding),NavProviderCopy.Text(p.CostId),p.CostRevision,p.GridCell);
                var s=source.Subscription;var subscription=new NavRouteSubscription(s.WorldGeneration,s.GroupId,s.RootOrderRevision,s.CommandSequence,s.IssuedTick,s.ActivationRevision,
                    s.EntityId,s.Incarnation,s.MemberOrderRevision,s.MobilityRevision,s.RequestSequence,Location(s.Origin,provider),Location(s.AssignedEndpoint,provider),NavProviderCopy.Text(s.HoldBinding),s.Provenance);
                if(source.Region!=null){
                    bool copied=region==null;if(copied)region=NavTerminalRegion.CopyOwned(source.Region,reservation);
                    region.StorageRoot.Retain();storage.Second=region.StorageRoot;if(copied)region.StorageRoot.ReleaseOwner();
                }
                var l=source.Limits;var limits=new NavGraphCompileLimits(l.MaxSamples,l.MaxNodes,l.MaxArcs);
                var value=new NavScheduledRequest(provider,clearance,profile,limits,region,subscription,source.MaxLabels,source.Singleton,source.Query,source.GroupQuery,source.DemandQuery,source.DemandQueryV2,source.DemandQueryV3,source.DemandQueryV4,source.DemandQueryV5,source.DemandQueryV6,source.LazyPartition,source.CertifiedLazyPartition,source.RectangularOrigins,source.TerminalRectangleProof,source.TerminalCornerProof,source.OriginShadowProof,source.TerminalCostProof,source.CertifiedGroupCorridor){ownedRecipe=storage};
                if(value.GraphKey!=source.GraphKey||value.CompileKey!=source.CompileKey||value.ArtifactKey!=source.ArtifactKey||value.Subscription.StableKey!=s.StableKey)
                    throw new ArgumentException("Owned recipe identity changed.");
                storage.Facade=value;clearance.MetadataOwner=storage;profile.MetadataOwner=storage;subscription.MetadataOwner=storage;limits.MetadataOwner=storage;storage.Retain();return value;
            }
        }
        private static NavLocation Location(NavLocation source,ILayeredNavigationProvider provider)
        {
            for(int n=0;n<provider.Surfaces.Count;n++){var surface=provider.Surfaces[n];if(surface.Id==source.SurfaceId)return surface.OwnedLocation(source.Position);}
            throw new ArgumentException("Owned recipe has an unknown surface.");
        }
    }
}

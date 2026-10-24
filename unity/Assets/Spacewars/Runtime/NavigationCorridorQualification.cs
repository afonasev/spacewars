using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal readonly struct NavigationCorridorGroupSource
    {
        internal readonly bool Exists;internal readonly long RootRevision,CommandSequence,IssuedTick;
        internal NavigationCorridorGroupSource(GroupOrderState group){Exists=group!=null;RootRevision=group?.OrderRevision??0;CommandSequence=group?.CommandSequence??0;IssuedTick=group?.IssuedTick??0;}
    }
    public sealed partial class NavigationSession
    {
        private static bool QualificationFailure(out string reason,string failure){reason=failure;return false;}
        // Pure qualifier: the caller supplies an immutable provider and observed
        // current group facts. Refusal never changes live diagnostics.
        private static bool QualifyCorridorPrepared(NavigationRequest request,NavPoint[] route,NavCorridorDescriptor corridor,
            ILayeredNavigationProvider provider,bool current,long generation,string surfaceProviderId,
            NavigationCorridorGroupSource group,out string reason)
        {
            reason=null;
            if(corridor==null)return request.CertifiedProviderBinding==null||
                QualificationFailure(out reason,"missing certified sidecar"); // Explicit legacy solver fallback.
            if(!current&&request.Session!=generation){
                var oldIdentity=request.MemberIdentity;
                if(corridor.World!=request.Session||corridor.Request!=request.Request||
                    corridor.Entity!=request.Entity||corridor.Order!=request.Order||
                    corridor.Group!=(oldIdentity?.GroupId??0)||
                    corridor.ProfileBinding!=NavigationAdmission.ExactProfileBinding(request.Profile)||
                    corridor.Radius!=request.Profile.Radius||corridor.GridCell!=request.Profile.GridCell||
                    corridor.HoldBinding!=request.HeldIdentity||
                    corridor.GraphKey!=new NavGraphBinding(corridor.ProviderBinding,
                        new NavClearanceProfile("ground",1,"ground",(int)(oldIdentity?.MobilityRevision??0),corridor.Radius),
                        new NavGraphProfile(corridor.ProfileBinding,"distance",1,corridor.GridCell)).StableKey||
                    !corridor.ProjectedRoute().SequenceEqual(route))return QualificationFailure(out reason,"stale historical binding");
                return true;
            }
            var identity=request.MemberIdentity;
            var clearance=new NavClearanceProfile("ground",1,"ground",(int)(identity?.MobilityRevision??0),request.Profile.Radius);
            var origin=request.StartLocation??(provider is PartitionNavigationProvider partitionOrigin&&
                partitionOrigin.TryLocate(request.Start,clearance,out var locatedOrigin)
                ?locatedOrigin:new NavLocation(request.Start,NavLocation.FlatSurface));
            var endpoint=request.GoalLocation??(provider is PartitionNavigationProvider partitionGoal&&
                partitionGoal.TryLocate(request.Goal,clearance,out var locatedGoal)
                ?locatedGoal:new NavLocation(request.Goal,NavLocation.FlatSurface));
            var expectedProvenance=request.Entity<0?NavRouteProvenance.ProducerProbe:
                identity!=null&&identity.GroupId!=0?NavRouteProvenance.CommandMember:NavRouteProvenance.LegacySingleton;
            if(corridor.World!=generation||corridor.Entity!=request.Entity||corridor.Request!=request.Request||
                corridor.Order!=request.Order||corridor.Group!=(identity?.GroupId??0)||
                corridor.Provenance!=expectedProvenance||
                corridor.ActivationRevision!=(identity?.OrderRevision??0)||
                corridor.Incarnation!=(identity?.Incarnation??0)||corridor.Mobility!=(identity?.MobilityRevision??0)||
                corridor.TopologyRevision!=request.BaseGeometry.Revision)
                return QualificationFailure(out reason,"identity/topology");
            if(
                corridor.ProfileBinding!=NavigationAdmission.ExactProfileBinding(request.Profile)||
                corridor.Radius!=request.Profile.Radius||corridor.GridCell!=request.Profile.GridCell||
                corridor.HoldBinding!=request.HeldIdentity||
                !corridor.Origin.Equals(origin)||!corridor.Endpoint.Equals(endpoint)||
                corridor.SurfaceProviderId!=surfaceProviderId)
                return QualificationFailure(out reason,"profile/endpoints");
            if(corridor.ProviderBinding!=provider.Binding)return QualificationFailure(out reason,"provider");
            if(
                corridor.GraphKey!=new NavGraphBinding(provider.Binding,clearance,
                    new NavGraphProfile(corridor.ProfileBinding,"distance",1,request.Profile.GridCell)).StableKey)
                return QualificationFailure(out reason,"graph");
            if(!corridor.ProjectedRoute().SequenceEqual(route))return QualificationFailure(out reason,"XZ projection");
            if(current&&corridor.Group!=0&&
                (!group.Exists||corridor.RootRevision!=group.RootRevision||
                corridor.CommandSequence!=group.CommandSequence||corridor.IssuedTick!=group.IssuedTick))return QualificationFailure(out reason,"current root");
            NavLocation at=corridor.Origin;
            foreach(var leg in corridor.Legs){
                if(!leg.From.Equals(at)||leg.Section!=NavCorridorSection.IndividualConnector||
                    leg.Branch!=0||leg.DownstreamMergeLeg!=0)return QualificationFailure(out reason,"leg shape");
                if(leg.Kind==NavTypedLegKind.Surface){
                    double cost=LayeredNavigationProvider.Distance(leg.From.Position,leg.To.Position)*
                        provider.SurfaceCost(leg.From.SurfaceId);
                    bool surfaceValid=provider is PartitionNavigationProvider
                        ?provider.IsValid(leg.From,clearance)&&provider.IsValid(leg.To,clearance)&&
                            request.Geometry.SegmentFree(leg.From.Position,leg.To.Position,clearance.Radius)
                        :provider.CanSweep(leg.From,leg.To,clearance);
                    if(!surfaceValid||Math.Abs(leg.Cost-cost)>1e-7)return QualificationFailure(out reason,"surface clearance/cost");
                }else if(!leg.From.Position.Equals(leg.To.Position)||leg.Cost!=0||
                    !(provider is PartitionNavigationProvider partition)||
                    !partition.Seams.Any(s=>s.Allowed&&s.Id==leg.PortalId&&
                        ((s.FromSurface==leg.From.SurfaceId&&s.ToSurface==leg.To.SurfaceId)||
                         (s.ToSurface==leg.From.SurfaceId&&s.FromSurface==leg.To.SurfaceId))&&
                        OnCorridorSeam(leg.From.Position,s.From,s.To)))return QualificationFailure(out reason,"portal seam");
                at=leg.To;
            }
            if(!at.Equals(corridor.Endpoint))return QualificationFailure(out reason,"terminal continuity");
            if(provider is PartitionNavigationProvider actualProvider){
                at=corridor.Origin;int portalIndex=0;
                foreach(var point in route){
                    if(!actualProvider.TryTrace(at,point,clearance,out var reached,out var trace))
                        return QualificationFailure(out reason,"XZ replay trace");
                    foreach(var leg in trace){
                        if(leg.Kind!=NavTypedLegKind.Portal)continue;
                        while(portalIndex<corridor.Legs.Count&&corridor.Legs[portalIndex].Kind!=NavTypedLegKind.Portal)portalIndex++;
                        if(portalIndex==corridor.Legs.Count||corridor.Legs[portalIndex++].PortalId!=leg.PortalId)
                            return QualificationFailure(out reason,"XZ portal sequence");
                    }
                    at=reached;
                }
                while(portalIndex<corridor.Legs.Count&&corridor.Legs[portalIndex].Kind!=NavTypedLegKind.Portal)portalIndex++;
                if(!at.Equals(corridor.Endpoint)||portalIndex!=corridor.Legs.Count)
                    return QualificationFailure(out reason,"XZ portal sequence");
            }
            return true;
        }
        private void PrepareOwnedCorridorQualification(NavigationPreparedAnswerNames candidate,int index,NavigationRetentionBudget.Scope scope)
        {
            var answer=candidate.Answers[index];var request=answer.Request;var owned=answer.StorageOwner;
            var corridor=owned?.CorridorOwner;var providerOwner=corridor?.ProviderOwner;
            if(corridor==null||providerOwner==null)return;
            if(!ReferenceEquals(owned.RequestOwner,request.StorageOwner)||!ReferenceEquals(corridor.RequestOwner,request.StorageOwner)||
                !ReferenceEquals(corridor.Corridor,answer.Corridor)||!ReferenceEquals(providerOwner.Geometry,request.Geometry)||
                request.CertifiedProviderBinding!=providerOwner.Provider.Binding||providerOwner.SurfaceProviderId!=(terrain?.Id??NavLocation.FlatSurface))
                throw new InvalidOperationException("Ready qualification lost its exact physical provider/request.");
            NavigationCorridorGroupSource group;
            using(EnterTransport()){
                if(candidate.Operation!=authorityOperationIdentity||!ReferenceEquals(candidate.Backing,groupCommandBacking)||
                    !ReferenceEquals(candidate.ObservedAdmission,admissionOwner))throw new InvalidOperationException("Ready qualification source changed.");
                group=ObserveCorridorGroup(request);
                if(group.Exists){
                    if(!(groups is NavigationCommandSlots<long,GroupOrderState> slots)||
                        !(slots.OwnerFor(answer.Corridor.Group)is NavigationLiveGroup owner))return;
                    candidate.GroupObservations[index]=owner.ObserveForCopy(ObserveLiveWriteToken());
                    candidate.RequireEdge(owner);candidate.RequireEdge(candidate.GroupObservations[index].Version);
                }
            }
            using(NavStorageScratch.Reserve(scope.OwnedReservation(),CorridorQualificationScratch(request,answer.Corridor,providerOwner.Provider,answer.RouteLength))){
                candidate.Qualified[index]=QualifyCorridorPrepared(request,answer.ReadOnlyRouteBacking,answer.Corridor,
                    providerOwner.Provider,true,Generation,providerOwner.SurfaceProviderId,group,out var failure);
                candidate.QualificationFailures[index]=failure;candidate.HasQualification[index]=true;
            }
        }
        private bool QualifyPreparedOrLegacy(NavigationPreparedAnswerNames prepared,NavigationAnswer answer,
            System.Collections.Generic.Dictionary<Tuple<NavGeometry,string>,ILayeredNavigationProvider> cache)
        {
            if(prepared!=null&&prepared.TryQualified(answer,out bool valid,out var reason)){LastCorridorFailure=reason;return valid;}
            return QualifyCorridor(answer.Request,answer.ReadOnlyRouteBacking,answer.Corridor,cache,true);
        }
        internal static long ReadyInstallationHeadroom(NavigationAnswer answer,int actors,int barrier,long publication)
        {
            var provider=answer.StorageOwner?.CorridorOwner?.ProviderOwner?.Provider;
            long qualifier=provider==null||answer.Corridor==null?0:CorridorQualificationScratch(answer.Request,answer.Corridor,provider,answer.RouteLength);
            // One complete single-answer installation (the authority already has
            // a FIFO single fallback), plus a birth and the complete live carrier.
            // Historical request/corridor/provider owners are shared, never copied.
            return checked(BirthHeadroom(actors)+publication+NavigationRetentionBudget.ScopeBytes+
                NavStorageLayout.Charge(NavigationPreparedAnswerNames.Layout(1,barrier))+
                NavStorageLayout.Charge(NavigationPreparedCrowdNameWrite.Layout)+
                NavStorageLayout.Charge(NavPreparedUnitRoute.Layout)+
                NavStorageLayout.Charge(96L+16L*(answer.RouteLength+1L))+
                NavStorageLayout.Charge(NavigationInstalledExecutionOwner.Layout)+
                2*NavStorageScratch.LeaseBytes+32L+8L*barrier+qualifier);
        }
        private static long CorridorQualificationScratch(NavigationRequest request,NavCorridorDescriptor corridor,ILayeredNavigationProvider provider,int routeCount)
        {
            long profileWire=checked(NavContract.StringWireBytes(request.Profile.ProfileId)+4L+8L*request.Profile.WireValueCount);
            long graphWire=checked(NavContract.StringWireBytes("layered-graph-v1")+NavContract.StringWireBytes(provider.Binding)+
                2L*NavContract.StringWireBytes("ground")+16L+NavContract.StringWireBytes(corridor.ProfileBinding)+NavContract.StringWireBytes("distance")+12L);
            long capacity=4;while(capacity<corridor.Legs.Count)capacity=checked(capacity*2);
            return checked(NavContract.DigestScratchBytes(profileWire)+NavContract.DigestScratchBytes(graphWire)+
                4096L+512L*(corridor.Legs.Count+(long)routeCount+1)+64L*capacity+
                (provider is PartitionNavigationProvider partition?partition.TraceScratchBound:0));
        }
    }
}

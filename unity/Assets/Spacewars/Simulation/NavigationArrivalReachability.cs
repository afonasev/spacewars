using System;
using System.IO;
using System.Linq;

namespace Spacewars.Simulation
{
    public sealed class NavArrivalReachabilityProof
    {
        internal NavArrivalReachabilityProof(NavArrivalInput input, NavArrivalMember member,
            NavLocation endpoint, NavScheduledRequest request, NavTypedRoute route)
        {
            if (!Verify(input, member, endpoint, request, route)) throw new ArgumentException("Invalid exact arrival route proof.");
            MemberKey = member.StableKey; Endpoint = endpoint; Radius = member.Radius;
            ProviderBinding = input.ProviderIdentity; TopologyIdentity = input.TopologyIdentity;
            ProfileIdentity = input.ProfileIdentity; GraphBinding = request.GraphKey;
            Subscription = request.Subscription; OccupancyBinding = input.Occupancy.StableKey; Route = route;
            StableKey = NavContract.Digest(w => {
                w.Write(MemberKey); NavContract.Location(w, Endpoint); w.Write(Radius); w.Write(ProviderBinding);
                w.Write(TopologyIdentity); w.Write(ProfileIdentity); w.Write(GraphBinding);
                w.Write(Subscription.StableKey); w.Write(OccupancyBinding); w.Write(route.Legs.Count);
                foreach (var leg in route.Legs) {
                    NavContract.Location(w, leg.From); NavContract.Location(w, leg.To); w.Write(leg.Cost);
                    w.Write((int)leg.Kind); w.Write(leg.PortalId ?? "");
                }
            });
        }
        public string MemberKey { get; } public NavLocation Endpoint { get; } public double Radius { get; }
        public string ProviderBinding { get; } public string TopologyIdentity { get; } public string ProfileIdentity { get; }
        public string GraphBinding { get; } public NavRouteSubscription Subscription { get; }
        public string OccupancyBinding { get; } public NavTypedRoute Route { get; } public string StableKey { get; }

        internal bool ValidateAgainst(NavArrivalInput input, NavArrivalMember member, NavLocation endpoint)
            => input != null && member != null && MemberKey == member.StableKey && Endpoint.Equals(endpoint) &&
               ProviderBinding == input.ProviderIdentity && TopologyIdentity == input.TopologyIdentity &&
               ProfileIdentity == input.ProfileIdentity && OccupancyBinding == input.Occupancy.StableKey &&
               Radius == member.Radius && Subscription.StableKey == Route.Subscription.StableKey &&
               Subscription.Origin.Equals(member.Origin) && Subscription.AssignedEndpoint.Equals(endpoint);

        internal static bool Verify(NavArrivalInput input, NavArrivalMember member, NavLocation endpoint,
            NavScheduledRequest request, NavTypedRoute route)
        {
            if (input == null || member == null || request == null || route == null ||
                request.Provider != input.Provider || request.Clearance.Radius != member.Radius ||
                route.Subscription.StableKey != request.Subscription.StableKey ||
                !request.Subscription.Origin.Equals(member.Origin) || !request.Subscription.AssignedEndpoint.Equals(endpoint) ||
                request.Subscription.EntityId != member.EntityId || request.Subscription.Incarnation != member.Incarnation ||
                request.Subscription.MemberOrderRevision != member.MemberOrderRevision ||
                request.Subscription.MobilityRevision != member.MobilityRevision ||
                request.Subscription.GroupId != member.GroupId || request.Subscription.WorldGeneration != member.WorldGeneration ||
                request.Subscription.RootOrderRevision != member.RootOrderRevision ||
                request.Subscription.CommandSequence != member.CommandSequence ||
                request.Subscription.IssuedTick != member.IssuedTick ||
                request.Subscription.ActivationRevision != member.ActivationRevision ||
                request.Subscription.RequestSequence != member.RequestSequence ||
                request.Subscription.HoldBinding != member.HoldBinding ||
                route.Legs.Count == 0) return false;
            var cursor = member.Origin;
            foreach (var leg in route.Legs) {
                if (!leg.From.Equals(cursor) || leg.Cost < 0 || double.IsNaN(leg.Cost) || double.IsInfinity(leg.Cost) ||
                    !Enum.IsDefined(typeof(NavTypedLegKind), leg.Kind) ||
                    leg.Kind == NavTypedLegKind.Portal && string.IsNullOrWhiteSpace(leg.PortalId) ||
                    leg.Kind == NavTypedLegKind.Surface && leg.PortalId != null) return false;
                cursor = leg.To;
            }
            return cursor.Equals(endpoint);
        }
    }
}

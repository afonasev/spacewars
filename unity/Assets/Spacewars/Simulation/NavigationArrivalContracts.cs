using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spacewars.Simulation
{
    public enum NavArrivalStatus { Pending, BudgetExhausted, Ready, NoAvailableSlot, UnreachableInGraph, Stale, InvalidInput, CapacityExceeded }
    public enum NavArrivalOccupantKind { Actor, PendingReservation, InstalledReservation }
    public enum NavArrivalReservationKind { PendingRequest, InstalledExecution }

    public sealed class NavArrivalMember
    {
        public NavArrivalMember(long entityId, long incarnation, long order, long mobilityRevision,
            NavLocation origin, NavClearanceProfile clearance, long worldGeneration, long groupId,
            long rootOrderRevision, long commandSequence, long issuedTick, long activationRevision,
            long memberOrderRevision, long requestSequence, string holdBinding = null)
        {
            if (entityId < 0 || incarnation < 1 || order < 0 || mobilityRevision < 0 || worldGeneration < 1 ||
                groupId < 1 || rootOrderRevision < 1 || commandSequence < 0 || issuedTick < 0 ||
                activationRevision < 1 || memberOrderRevision < 1 || requestSequence < 1 || clearance == null ||
                holdBinding != null && holdBinding.Length > 256)
                throw new ArgumentException("Invalid arrival member identity.");
            EntityId = entityId; Incarnation = incarnation; Order = order; MobilityRevision = mobilityRevision;
            Origin = origin; Clearance = clearance; WorldGeneration = worldGeneration; GroupId = groupId;
            RootOrderRevision = rootOrderRevision; CommandSequence = commandSequence; IssuedTick = issuedTick;
            ActivationRevision = activationRevision; MemberOrderRevision = memberOrderRevision;
            RequestSequence = requestSequence; HoldBinding = holdBinding;
            StableKey = NavContract.Digest(w => {
                foreach (var value in new[] { EntityId, Incarnation, Order, MobilityRevision, WorldGeneration,
                    GroupId, RootOrderRevision, CommandSequence, IssuedTick, ActivationRevision,
                    MemberOrderRevision, RequestSequence }) w.Write(value);
                NavContract.Location(w, Origin); Clearance.Write(w); w.Write(HoldBinding != null);
                if (HoldBinding != null) w.Write(HoldBinding);
            });
        }
        public long EntityId { get; } public long Incarnation { get; } public long Order { get; }
        public long MobilityRevision { get; } public NavLocation Origin { get; } public NavClearanceProfile Clearance { get; }
        public double Radius => Clearance.Radius; public long WorldGeneration { get; } public long GroupId { get; }
        public long RootOrderRevision { get; } public long CommandSequence { get; } public long IssuedTick { get; }
        public long ActivationRevision { get; } public long MemberOrderRevision { get; }
        public long RequestSequence { get; } public string HoldBinding { get; } public string StableKey { get; }
    }

    // Occupancy facts are a copied view. Reservation rows deliberately describe
    // intent/execution identity; neither kind certifies a reachable route.
    public sealed class NavArrivalOccupant
    {
        public NavArrivalOccupant(NavArrivalOccupantKind kind, long actorId, long incarnation,
            NavLocation location, double radius, bool blocksPlacement, string identity,
            NavArrivalReservationKind? reservationKind = null, long requestSequence = 0,
            long historicalExecution = 0, NavLocation? actorTarget = null)
        {
            if (!Enum.IsDefined(typeof(NavArrivalOccupantKind), kind) || actorId < 0 || incarnation < 1 ||
                radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius) ||
                string.IsNullOrWhiteSpace(identity) || identity.Length > 256 ||
                (kind == NavArrivalOccupantKind.Actor) != !reservationKind.HasValue ||
                kind == NavArrivalOccupantKind.PendingReservation && (reservationKind != NavArrivalReservationKind.PendingRequest || requestSequence < 1) ||
                kind == NavArrivalOccupantKind.InstalledReservation && (reservationKind != NavArrivalReservationKind.InstalledExecution || historicalExecution < 1 || !actorTarget.HasValue))
                throw new ArgumentException("Invalid arrival occupancy row.");
            Kind = kind; ActorId = actorId; Incarnation = incarnation; Location = location; Radius = radius;
            BlocksPlacement = blocksPlacement; Identity = identity; ReservationKind = reservationKind;
            RequestSequence = requestSequence; HistoricalExecution = historicalExecution; ActorTarget = actorTarget;
            StableKey = NavContract.Digest(w => {
                w.Write((int)Kind); w.Write(ActorId); w.Write(Incarnation); NavContract.Location(w, Location);
                w.Write(Radius); w.Write(BlocksPlacement); w.Write(Identity); w.Write(ReservationKind.HasValue);
                if (ReservationKind.HasValue) w.Write((int)ReservationKind.Value);
                w.Write(RequestSequence); w.Write(HistoricalExecution); w.Write(ActorTarget.HasValue);
                if (ActorTarget.HasValue) NavContract.Location(w, ActorTarget.Value);
            });
        }
        public NavArrivalOccupantKind Kind { get; } public long ActorId { get; } public long Incarnation { get; }
        public NavLocation Location { get; } public double Radius { get; } public bool BlocksPlacement { get; }
        public string Identity { get; } public NavArrivalReservationKind? ReservationKind { get; }
        public long RequestSequence { get; } public long HistoricalExecution { get; } public NavLocation? ActorTarget { get; }
        public string StableKey { get; }
    }

    public sealed class NavArrivalOccupancySnapshot
    {
        private readonly NavArrivalOccupant[] rows;
        public NavArrivalOccupancySnapshot(long generation, IEnumerable<NavArrivalOccupant> occupants)
        {
            if (generation < 1 || occupants == null) throw new ArgumentException("Invalid occupancy snapshot.");
            Generation = generation; rows = occupants.ToArray();
            if (rows.Any(x => x == null) || rows.Select(x => x.StableKey).Distinct(StringComparer.Ordinal).Count() != rows.Length)
                throw new ArgumentException("Duplicate or null occupancy row.");
            Array.Sort(rows, (a, b) => { int c = a.ActorId.CompareTo(b.ActorId); if (c != 0) return c;
                c = a.Kind.CompareTo(b.Kind); return c != 0 ? c : string.CompareOrdinal(a.StableKey, b.StableKey); });
            Occupants = Array.AsReadOnly((NavArrivalOccupant[])rows.Clone());
            StableKey = NavContract.Digest(w => { w.Write(Generation); w.Write(rows.Length); foreach (var row in rows) w.Write(row.StableKey); });
        }
        public long Generation { get; } public IReadOnlyList<NavArrivalOccupant> Occupants { get; }
        public string StableKey { get; }
    }

    public sealed class NavArrivalCandidateSource
    {
        private readonly NavLocation[] candidates;
        public NavArrivalCandidateSource(string id, int revision, NavLocation requested, IEnumerable<NavLocation> candidates)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || revision < 1 || candidates == null)
                throw new ArgumentException("Invalid arrival candidate source.");
            Id = id; Revision = revision; Requested = requested; this.candidates = candidates.ToArray();
            if (this.candidates.Length == 0 || this.candidates.Any(c => c.SurfaceId == null) || this.candidates.Distinct().Count() != this.candidates.Length)
                throw new ArgumentException("Invalid candidate list.");
            Candidates = Array.AsReadOnly((NavLocation[])this.candidates.Clone());
            StableKey = NavContract.Digest(w => { w.Write(Id); w.Write(Revision); NavContract.Location(w, Requested);
                w.Write(this.candidates.Length); foreach (var candidate in this.candidates) NavContract.Location(w, candidate); });
        }
        public string Id { get; } public int Revision { get; } public NavLocation Requested { get; }
        public IReadOnlyList<NavLocation> Candidates { get; } public string StableKey { get; }
    }

    public sealed class NavArrivalInput
    {
        private readonly NavArrivalMember[] members;
        public NavArrivalInput(string topologyIdentity, string profileIdentity, ILayeredNavigationProvider provider,
            NavGraphProfile profile, NavigationProfile arrivalProfile, NavGraphCompileLimits limits, NavArrivalCandidateSource source,
            IEnumerable<NavArrivalMember> members, NavArrivalOccupancySnapshot occupancy, int maxLabels = 4096)
        {
            if (string.IsNullOrWhiteSpace(topologyIdentity) || topologyIdentity.Length > 256 ||
                string.IsNullOrWhiteSpace(profileIdentity) || profileIdentity.Length > 256 || provider == null ||
                profile == null || arrivalProfile == null || limits == null || source == null || members == null || occupancy == null || maxLabels < 1)
                throw new ArgumentException("Invalid arrival input.");
            TopologyIdentity = topologyIdentity; ProfileIdentity = profileIdentity; Provider = provider;
            Profile = profile; ArrivalProfile = arrivalProfile; Limits = limits; Source = source; Occupancy = occupancy; MaxLabels = maxLabels;
            var supplied = members.ToArray();
            if (supplied.Any(x => x == null)) throw new ArgumentException("Null arrival member.");
            this.members = supplied.OrderBy(x => x.Order).ThenBy(x => x.EntityId).ThenBy(x => x.Incarnation).ToArray();
            if (this.members.Length == 0 ||
                this.members.Select(x => x.EntityId).Distinct().Count() != this.members.Length ||
                this.members.Select(x => x.GroupId).Distinct().Count() != 1 ||
                this.members.Select(x => x.WorldGeneration).Distinct().Count() != 1 ||
                this.members.Any(x => x.WorldGeneration != occupancy.Generation)) throw new ArgumentException("Invalid ordered group members.");
            Members = Array.AsReadOnly((NavArrivalMember[])this.members.Clone());
            ProviderIdentity = provider.Binding;
            StableKey = NavContract.Digest(w => {
                w.Write(TopologyIdentity); w.Write(ProfileIdentity); w.Write(ProviderIdentity);
                w.Write(Profile.ExactProfileBinding); w.Write(Profile.CostId); w.Write(Profile.CostRevision); w.Write(Profile.GridCell);
                w.Write(ArrivalProfile.ArrivalSlotSpacing); w.Write(ArrivalProfile.ArrivalTolerance); w.Write(ArrivalProfile.LocalGridCell);
                w.Write(source.StableKey); w.Write(occupancy.StableKey); w.Write(maxLabels); w.Write(this.members.Length);
                foreach (var member in this.members) w.Write(member.StableKey);
                w.Write(limits.MaxSamples); w.Write(limits.MaxNodes); w.Write(limits.MaxArcs);
            });
        }
        public string TopologyIdentity { get; } public string ProfileIdentity { get; } public string ProviderIdentity { get; }
        public ILayeredNavigationProvider Provider { get; } public NavGraphProfile Profile { get; }
        public NavigationProfile ArrivalProfile { get; }
        public NavGraphCompileLimits Limits { get; } public NavArrivalCandidateSource Source { get; }
        public NavArrivalOccupancySnapshot Occupancy { get; } public IReadOnlyList<NavArrivalMember> Members { get; }
        public int MaxLabels { get; } public string StableKey { get; }
        internal NavArrivalMember[] CopyMembers() => (NavArrivalMember[])members.Clone();
    }

    public sealed class NavArrivalProposal
    {
        internal NavArrivalProposal(NavArrivalMember member, NavLocation endpoint, NavTypedRoute route,
            NavArrivalReachabilityProof proof)
        { Member = member; Endpoint = endpoint; Route = route; Proof = proof; }
        public NavArrivalMember Member { get; } public NavLocation Endpoint { get; } public NavTypedRoute Route { get; }
        public NavArrivalReachabilityProof Proof { get; }
    }
}

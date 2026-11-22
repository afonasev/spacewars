using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed class RecordedGroundSlot
    {
        public long Tick { get; }
        public int Ordinal { get; }
        public string Owner { get; }
        public long Generation { get; }
        public int Entity { get; }
        public long Request { get; }
        public long Order { get; }
        public NavLocation Endpoint { get; }

        public RecordedGroundSlot(long tick, int ordinal, string owner, long generation, int entity, long request,
            long order, NavLocation endpoint)
        {
            if (tick < 0 || ordinal < 0 || string.IsNullOrEmpty(owner) || generation < 1 || entity < 1 ||
                request < 1 || order < 1 || string.IsNullOrEmpty(endpoint.SurfaceId))
                throw new ArgumentException("Invalid recorded ground slot.");
            Tick = tick;
            Ordinal = ordinal;
            Owner = owner;
            Generation = generation;
            Entity = entity;
            Request = request;
            Order = order;
            Endpoint = endpoint;
        }
    }

    public sealed class RecordedGroundRoute
    {
        public long Tick { get; }
        public int Ordinal { get; }
        public string Owner { get; }
        public NavCorridorDescriptor Corridor { get; }
        public int Entity { get; }
        public long Request { get; }
        public long Order { get; }
        public NavSolveStatus Status { get; }
        public NavigationOutcome? Failure { get; }
        public IReadOnlyList<NavPoint> Points { get; }
        public RecordedRecastPlan Recast { get; }
        public bool Reissued { get; }

        public RecordedGroundRoute(long tick, int ordinal, string owner, int entity, long request, long order,
            NavSolveStatus status, NavCorridorDescriptor corridor, IEnumerable<NavPoint> points,
            RecordedRecastPlan recast, bool reissued, NavigationOutcome? failure)
        {
            if (tick < 0 || ordinal < 0 || string.IsNullOrEmpty(owner) || entity < 1 || request < 1 || order < 1 ||
                status == NavSolveStatus.Pending || !Enum.IsDefined(typeof(NavSolveStatus), status) ||
                (status == NavSolveStatus.Ready) != (corridor != null) || status != NavSolveStatus.Ready && !failure.HasValue ||
                reissued && (status != NavSolveStatus.Ready || failure.HasValue) ||
                failure.HasValue && failure != NavigationOutcome.Rejected && failure != NavigationOutcome.Unreachable)
                throw new ArgumentException("Invalid recorded ground route.");
            var path = points.ToArray();
            if (corridor == null ? path.Length != 0 || recast != null :
                corridor.Entity != entity || corridor.Request != request || corridor.Order != order ||
                path.Length == 0 || !corridor.ProjectedRoute().SequenceEqual(path))
                throw new ArgumentException("Recorded points differ from the typed corridor.");
            Tick = tick;
            Ordinal = ordinal;
            Owner = owner;
            Entity = entity;
            Request = request;
            Order = order;
            Status = status;
            Failure = failure;
            Corridor = corridor;
            Points = Array.AsReadOnly(path);
            Recast = recast;
            Reissued = reissued;
        }
    }

    public sealed class RecordedRecastPlan
    {
        public string Binding { get; }
        public string MapBinding { get; }
        public int Join { get; }
        public int Exit { get; }
        public int OriginLegs { get; }
        public int TerminalLeg { get; }
        public int FanNode { get; }
        public NavPoint FanCenter { get; }
        public double FanRadius { get; }
        public IReadOnlyList<NavCorridorLeg> SpineLegs { get; }

        public RecordedRecastPlan(string binding, string mapBinding, int join, int exit, int originLegs,
            int terminalLeg, int fanNode, NavPoint fanCenter, double fanRadius, IEnumerable<NavCorridorLeg> spineLegs)
        {
            var legs = spineLegs.ToArray();
            if (legs.Length == 0 || legs.Length > 16384 || join < 0 || exit < join || exit > legs.Length ||
                originLegs < 0 || terminalLeg != originLegs + exit - join || fanNode < 0 ||
                fanNode > legs.Length || !NavRecastSpine.Finite(fanRadius) || fanRadius <= 0)
                throw new ArgumentException("Invalid recorded Recast plan.");
            NavContract.Sha256(binding);
            NavContract.Sha256(mapBinding);
            Binding = binding;
            MapBinding = mapBinding;
            Join = join;
            Exit = exit;
            OriginLegs = originLegs;
            TerminalLeg = terminalLeg;
            FanNode = fanNode;
            FanCenter = fanCenter;
            FanRadius = fanRadius;
            SpineLegs = Array.AsReadOnly(legs);
        }
    }

    internal sealed class RecordedGroundReplay : IDisposable
    {
        private readonly Queue<RecordedGroundRoute> remaining;
        private readonly Queue<RecordedGroundSlot> slots;
        private readonly Queue<RecordedGroundRoute> awaitingAcceptance = new Queue<RecordedGroundRoute>();
        private readonly CertifiedRouteKernel transport = new CertifiedRouteKernel();
        private readonly PlayableDomain domain;
        private long observedTick = -1;
        private int observedOrdinal = -1;

        internal RecordedGroundReplay(PlayableDomain domain, IEnumerable<RecordedGroundRoute> routes,
            IEnumerable<RecordedGroundSlot> slots)
        {
            this.domain = domain;
            remaining = new Queue<RecordedGroundRoute>(routes ?? throw new ArgumentNullException(nameof(routes)));
            this.slots = new Queue<RecordedGroundSlot>(slots ?? throw new ArgumentNullException(nameof(slots)));
            domain.Navigation.ReadyRouteAccepted += VerifyAcceptance;
            domain.Navigation.ReadyRouteReissued += VerifyReissue;
            domain.Navigation.GroundRouteFailed += VerifyFailure;
            domain.Navigation.RouteRequested += VerifySlot;
        }

        internal int Remaining => remaining.Count + awaitingAcceptance.Count;
        internal int RemainingSlots => slots.Count;

        internal NavLocation? NextSlot(string owner, int entity)
        {
            if (slots.Count == 0) return null;
            var slot = slots.Peek();
            return slot.Tick == domain.Tick && slot.Owner == owner && slot.Entity == entity
                ? slot.Endpoint : (NavLocation?)null;
        }

        internal NavPoint[] MoveSlots(string owner, int[] ids)
        {
            if (!NextSlot(owner, ids[0]).HasValue) return null;
            var points = new NavPoint[ids.Length];
            int i = 0;
            foreach (var slot in slots)
            {
                if (slot.Tick != domain.Tick || slot.Owner != owner || slot.Entity != ids[i])
                    throw new InvalidOperationException("Recorded ground recipients differ from the movement command.");
                points[i++] = slot.Endpoint.Position;
                if (i == points.Length) return points;
            }
            throw new InvalidOperationException("Recorded ground slots are incomplete.");
        }

        private void VerifySlot(NavigationRequest request)
        {
            string owner = domain.RouteJournalOwner(request.Entity);
            if (owner == null || !request.GoalLocation.HasValue) return;
            if (slots.Count == 0)
                throw new InvalidOperationException("An unrecorded ground slot was issued during replay.");
            var expected = slots.Dequeue();
            if (expected.Tick != domain.Tick || expected.Owner != owner || expected.Generation != request.Session ||
                expected.Entity != request.Entity || expected.Request != request.Request || expected.Order != request.Order ||
                !expected.Endpoint.Equals(request.GoalLocation.Value))
                throw new InvalidOperationException("Ground slot issuance diverged from the recording.");
            ObserveDecision(expected.Tick, expected.Ordinal);
        }

        internal void Deliver()
        {
            var navigation = domain.Navigation;
            var binding = new PlayableRouteBinding(domain.Geometry, domain.RoutingProfile, navigation.Admission,
                navigation.RoutePort);
            while (transport.RetirePhysicalDelivered(binding)) { }
            while (transport.DropStaleRecordedIngress(binding.RoutePort)) { }
            while (remaining.Count != 0 && remaining.Peek().Tick <= domain.Tick)
            {
                var route = remaining.Peek();
                if (route.Tick != domain.Tick || route.Owner != domain.RouteJournalOwner(route.Entity))
                    throw new InvalidOperationException("Recorded ground delivery tick or owner diverged.");
                SelectIngress(navigation, route.Request);
                if (!transport.TryRegisterPhysicalOrdinary(binding, route) || !transport.FlushPhysicalOrdinary(binding))
                    throw new InvalidOperationException("Recorded ground answer could not enter ordinary transport.");
                awaitingAcceptance.Enqueue(remaining.Dequeue());
            }
        }

        private void SelectIngress(NavigationSession navigation, long request)
        {
            int count = navigation.Requests.Count;
            while (count-- > 0 && navigation.Requests.TryPeek(out var head))
            {
                if (head.Request == request) return;
                if (transport.DropStaleRecordedIngress(navigation.RoutePort)) continue;
                if (!navigation.RotateRecordedGroundIngress(head)) break;
            }
            throw new InvalidOperationException("Recorded ground request is absent at delivery.");
        }

        private void VerifyAcceptance(NavigationAnswer answer) => VerifyDelivery(answer, reissued: false, failure: null);
        private void VerifyReissue(NavigationAnswer answer) => VerifyDelivery(answer, reissued: true, failure: null);
        private void VerifyFailure(NavigationAnswer answer, NavigationOutcome outcome) =>
            VerifyDelivery(answer, reissued: false, failure: outcome);

        private void VerifyDelivery(NavigationAnswer answer, bool reissued, NavigationOutcome? failure)
        {
            if (awaitingAcceptance.Count == 0)
                throw new InvalidOperationException("An unrecorded ground route was accepted during replay.");
            var expected = awaitingAcceptance.Dequeue();
            if (expected.Tick != domain.Tick || reissued != expected.Reissued || expected.Failure != failure ||
                answer.Request.Request != expected.Request || answer.Status != expected.Status ||
                answer.Corridor?.StableKey != expected.Corridor?.StableKey)
                throw new InvalidOperationException("Ground route delivery diverged from the recorded result.");
            ObserveDecision(expected.Tick, expected.Ordinal);
        }

        private void ObserveDecision(long tick, int ordinal)
        {
            if (tick < observedTick || tick == observedTick && ordinal <= observedOrdinal)
                throw new InvalidOperationException("Ground replay changed decision order within a tick.");
            observedTick = tick;
            observedOrdinal = ordinal;
        }

        internal void CheckDelivered()
        {
            if (awaitingAcceptance.Count != 0)
                throw new InvalidOperationException("A recorded ground route missed its acceptance tick.");
        }

        public void Dispose()
        {
            domain.Navigation.ReadyRouteAccepted -= VerifyAcceptance;
            domain.Navigation.ReadyRouteReissued -= VerifyReissue;
            domain.Navigation.GroundRouteFailed -= VerifyFailure;
            domain.Navigation.RouteRequested -= VerifySlot;
            transport.Clear();
        }
    }

    internal sealed partial class PlayableDomain
    {
        internal RecordedGroundReplay GroundReplay;
    }

    public sealed partial class PlayableAuthorityTick
    {
        public void UseRecordedGroundRoutes(IEnumerable<RecordedGroundRoute> routes, IEnumerable<RecordedGroundSlot> slots)
        {
            if (Tick != 0 || domain.Navigation.PendingCount != 0 || domain.Navigation.HasCertifiedHost ||
                domain.GroundReplay != null)
                throw new InvalidOperationException("Ground route replay requires a fresh authority without a planner.");
            tickQuanta = false;
            SetRouteDeliveryPolicy(asynchronous: true);
            domain.GroundReplay = new RecordedGroundReplay(domain, routes, slots);
        }

        public int RemainingRecordedGroundRoutes => domain.GroundReplay?.Remaining ?? 0;
        public int RemainingRecordedGroundSlots => domain.GroundReplay?.RemainingSlots ?? 0;
    }
}

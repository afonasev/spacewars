using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Spacewars.Simulation
{
    internal static class NavContract
    {
        internal static string Text(string value)
        { if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException("Invalid navigation identity."); return value; }
        internal static string Sha256(string value)
        { if (value == null || value.Length != 64 || value.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))) throw new ArgumentException("Exact binding must be a lowercase SHA256 digest."); return value; }
        internal static double Number(double value, bool positive = false)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || positive && value == 0) throw new ArgumentException("Invalid navigation quantity."); return value; }
        internal static int Revision(int value)
        { if (value < 1) throw new ArgumentException("Invalid navigation revision."); return value; }
        internal static long Sequence(long value)
        { if (value < 1) throw new ArgumentException("Invalid navigation sequence."); return value; }
        internal static string Digest(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) write(writer);
                using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
        internal static void Location(BinaryWriter writer, NavLocation location)
        { Text(location.SurfaceId); writer.Write(location.SurfaceId); Point(writer, location.Position); }
        internal static void Point(BinaryWriter writer, NavPoint point)
        { if (double.IsNaN(point.X) || double.IsInfinity(point.X) || double.IsNaN(point.Z) || double.IsInfinity(point.Z)) throw new ArgumentException("Invalid navigation point."); writer.Write(point.X); writer.Write(point.Z); }
        internal static void Polygon(BinaryWriter writer, NavPolygon polygon)
        { writer.Write(polygon.Vertices.Count); foreach (var point in polygon.Vertices) Point(writer, point); }
    }

    public sealed class NavClearanceProfile
    {
        public NavClearanceProfile(string id, int revision, string mobilityClass, int mobilityRevision, double radius)
        { Id = NavContract.Text(id); Revision = NavContract.Revision(revision); MobilityClass = NavContract.Text(mobilityClass);
          if (mobilityRevision < 0) throw new ArgumentException("Invalid mobility revision.");
          MobilityRevision = mobilityRevision; Radius = NavContract.Number(radius); }
        public string Id { get; } public int Revision { get; }
        public string MobilityClass { get; } public int MobilityRevision { get; } public double Radius { get; }
        internal void Write(BinaryWriter w) { w.Write(Id); w.Write(Revision); w.Write(MobilityClass); w.Write(MobilityRevision); w.Write(Radius); }
    }

    // Physical distance weights and portal traversal costs are authored data,
    // never inferred from display IDs or installed process hash codes.
    public sealed class NavGraphProfile
    {
        public NavGraphProfile(string exactProfileBinding, string costId, int costRevision, double gridCell)
        { ExactProfileBinding = NavContract.Sha256(exactProfileBinding); CostId = NavContract.Text(costId); CostRevision = NavContract.Revision(costRevision); GridCell = NavContract.Number(gridCell, true); }
        public string ExactProfileBinding { get; } public string CostId { get; } public int CostRevision { get; } public double GridCell { get; }
        internal void Write(BinaryWriter w) { w.Write(ExactProfileBinding); w.Write(CostId); w.Write(CostRevision); w.Write(GridCell); }
    }

    public sealed class NavGraphBinding : IEquatable<NavGraphBinding>
    {
        internal NavGraphBinding(string providerBinding, NavClearanceProfile clearance, NavGraphProfile profile)
        {
            ProviderBinding = providerBinding; Clearance = clearance; Profile = profile;
            StableKey = NavContract.Digest(w => { w.Write("layered-graph-v1"); w.Write(providerBinding); clearance.Write(w); profile.Write(w); });
        }
        public string ProviderBinding { get; } public NavClearanceProfile Clearance { get; } public NavGraphProfile Profile { get; } public string StableKey { get; }
        public bool Equals(NavGraphBinding other) => other != null && StableKey == other.StableKey;
        public override bool Equals(object other) => Equals(other as NavGraphBinding);
        // Dictionary acceleration only; StableKey/Equals supply exact binding.
        public override int GetHashCode() => StableKey.GetHashCode();
    }

    public enum NavTerminalKind { Region, Portal }
    public sealed class NavTerminalRegion
    {
        public NavTerminalRegion(string id, int revision, NavLocation requested, IEnumerable<NavLocation> candidates)
        {
            Id = NavContract.Text(id); Revision = NavContract.Revision(revision); NavContract.Text(requested.SurfaceId); Requested = requested;
            var copy = (candidates ?? throw new ArgumentNullException(nameof(candidates))).Take(65).ToArray();
            if (copy.Length == 0 || copy.Length > 64 || copy.Any(c => c.SurfaceId != requested.SurfaceId) || copy.Distinct().Count() != copy.Length) throw new ArgumentException("Invalid finite terminal region.");
            Candidates = Array.AsReadOnly(copy);
            StableKey = NavContract.Digest(w => { w.Write(Id); w.Write(Revision); NavContract.Location(w, Requested); w.Write(copy.Length); foreach (var c in copy) NavContract.Location(w, c); });
        }
        public string Id { get; } public int Revision { get; } public NavLocation Requested { get; } public IReadOnlyList<NavLocation> Candidates { get; } public string StableKey { get; }
    }

    // No authority group/member fields: independent commands can reuse this
    // mathematical artifact without merging ownership or endpoint reservations.
    public sealed class NavSharedArtifactKey : IEquatable<NavSharedArtifactKey>
    {
        internal NavSharedArtifactKey(NavGraphBinding graph, string surface, int component, NavTerminalKind kind, string terminalBinding)
        {
            Graph = graph; SurfaceId = surface; Component = component; TerminalKind = kind; TerminalBinding = terminalBinding;
            StableKey = NavContract.Digest(w => { w.Write(graph.StableKey); w.Write(surface); w.Write(component); w.Write((int)kind); w.Write(terminalBinding); });
        }
        public NavGraphBinding Graph { get; } public string SurfaceId { get; } public int Component { get; }
        public NavTerminalKind TerminalKind { get; } public string TerminalBinding { get; } public string StableKey { get; }
        public bool Equals(NavSharedArtifactKey other) => other != null && StableKey == other.StableKey;
        public override bool Equals(object other) => Equals(other as NavSharedArtifactKey);
        public override int GetHashCode() => StableKey.GetHashCode();
    }

    // Root issuance and member/cohort activation are separate. No global
    // membership revision can invalidate an unaffected member subscription.
    // Future authority adapters must compare every field before publication.
    public enum NavRouteProvenance { CommandMember, LegacySingleton, ProducerProbe }
    public sealed class NavRouteSubscription
    {
        public NavRouteSubscription(long worldGeneration, long groupId, long rootOrderRevision, long commandSequence,
            long issuedTick, long activationRevision, long entityId, long incarnation, long memberOrderRevision,
            long mobilityRevision, long requestSequence, NavLocation origin, NavLocation assignedEndpoint, string holdBinding,
            NavRouteProvenance provenance = NavRouteProvenance.CommandMember)
        {
            if (!Enum.IsDefined(typeof(NavRouteProvenance), provenance) || worldGeneration < 0 || groupId < 0 ||
                rootOrderRevision < 0 || commandSequence < 0 || issuedTick < 0 || activationRevision < 0 ||
                incarnation < 0 || memberOrderRevision < 0 || mobilityRevision < 0 ||
                (provenance == NavRouteProvenance.ProducerProbe ? entityId >= 0 || groupId != 0 : entityId < 0) ||
                (provenance == NavRouteProvenance.CommandMember && groupId == 0))
                throw new ArgumentException("Invalid route provenance identity.");
            Provenance = provenance; WorldGeneration = worldGeneration; GroupId = groupId;
            RootOrderRevision = rootOrderRevision; CommandSequence = commandSequence; IssuedTick = issuedTick;
            ActivationRevision = activationRevision; EntityId = entityId; Incarnation = incarnation;
            MemberOrderRevision = memberOrderRevision; MobilityRevision = mobilityRevision;
            RequestSequence = NavContract.Sequence(requestSequence);
            NavContract.Text(origin.SurfaceId); NavContract.Text(assignedEndpoint.SurfaceId); Origin = origin; AssignedEndpoint = assignedEndpoint;
            if (holdBinding != null && holdBinding.Length > 256) throw new ArgumentException("Invalid HOLD identity.");
            HoldBinding = holdBinding;
            StableKey = NavContract.Digest(w => {
                w.Write((int)Provenance);
                foreach (var value in new[] { WorldGeneration, GroupId, RootOrderRevision, CommandSequence, IssuedTick, ActivationRevision, EntityId, Incarnation, MemberOrderRevision, MobilityRevision, RequestSequence }) w.Write(value);
                NavContract.Location(w, origin); NavContract.Location(w, assignedEndpoint);
                w.Write(HoldBinding != null); if (HoldBinding != null) w.Write(HoldBinding);
            });
        }
        public NavRouteProvenance Provenance { get; }
        public long WorldGeneration { get; } public long GroupId { get; } public long RootOrderRevision { get; } public long CommandSequence { get; } public long IssuedTick { get; }
        public long ActivationRevision { get; } public long EntityId { get; } public long Incarnation { get; } public long MemberOrderRevision { get; } public long MobilityRevision { get; } public long RequestSequence { get; }
        public NavLocation Origin { get; } public NavLocation AssignedEndpoint { get; } public string HoldBinding { get; } public string StableKey { get; }
    }
}

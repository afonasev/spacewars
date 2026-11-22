using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Recorded before applying the ground part of a mixed order, so replay also
    // reproduces an unavailable destination and a subsequent ground refusal.
    public sealed class RecordedAirAssignment
    {
        public long Tick { get; }
        public long Generation { get; }
        public long Sequence { get; }
        public string Owner { get; }
        public PlayableCommandKind Kind { get; }
        public PlayableOrderMode Mode { get; }
        public NavPoint Target { get; }
        public IReadOnlyList<int> Aircraft { get; }
        public IReadOnlyList<NavPoint> Destinations { get; }

        public RecordedAirAssignment(long tick, long generation, long sequence, string owner,
            PlayableCommandKind kind, PlayableOrderMode mode, NavPoint target,
            IReadOnlyList<int> aircraft, IReadOnlyList<NavPoint> destinations)
        {
            if (aircraft == null || aircraft.Count == 0 || (destinations != null && destinations.Count != aircraft.Count))
                throw new ArgumentException("Invalid recorded aircraft assignment.");
            Tick = tick;
            Generation = generation;
            Sequence = sequence;
            Owner = owner;
            Kind = kind;
            Mode = mode;
            Target = target;
            Aircraft = Array.AsReadOnly(aircraft.ToArray());
            Destinations = destinations == null ? null : Array.AsReadOnly(destinations.ToArray());
        }

        internal NavPoint[] Resolve(long tick, PlayableCommand command, int[] aircraft)
        {
            if (tick != Tick || command.Generation != Generation || command.Sequence != Sequence ||
                command.PlayerId != Owner || command.Kind != Kind || command.Mode != Mode ||
                !command.Target.Equals(Target) || !Aircraft.SequenceEqual(aircraft))
                throw new InvalidOperationException("Air assignment diverged from the recorded command.");
            return Destinations?.ToArray();
        }
    }

    public interface IAirAssignmentJournal
    {
        void AirAssignment(RecordedAirAssignment assignment);
    }

    internal sealed partial class PlayableDomain
    {
        internal Queue<RecordedAirAssignment> ReplayAirAssignments;
        internal Action<RecordedAirAssignment> AirAssignmentRecorded;
    }

    public sealed partial class PlayableAuthorityTick
    {
        public void UseRecordedAirAssignments(IEnumerable<RecordedAirAssignment> assignments)
        {
            domain.ReplayAirAssignments = new Queue<RecordedAirAssignment>(assignments ?? throw new ArgumentNullException(nameof(assignments)));
        }
        public int RemainingRecordedAirAssignments => domain.ReplayAirAssignments?.Count ?? 0;
    }
}

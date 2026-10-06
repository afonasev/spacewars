namespace Spacewars.Simulation
{
    public enum RuntimeStatus { Starting, Running, Paused, Stopped, Failed }

    public sealed class FoundationEntitySnapshot
    {
        public FoundationEntitySnapshot(string entityId, double x, double z, bool moving) { EntityId = entityId; X = x; Z = z; Moving = moving; }
        public string EntityId { get; private set; }
        public double X { get; private set; }
        public double Z { get; private set; }
        public bool Moving { get; private set; }
    }

    public sealed class FoundationSnapshot
    {
        public FoundationSnapshot(string profileId, int profileRevision, int seed, long generation, long snapshotSequence, long tick, bool paused, RuntimeStatus status, string failure, double lastTickMilliseconds, FoundationEntitySnapshot entity)
        {
            ProfileId = profileId; ProfileRevision = profileRevision; Seed = seed; Generation = generation; SnapshotSequence = snapshotSequence;
            Tick = tick; Paused = paused; Status = status; Failure = failure; LastTickMilliseconds = lastTickMilliseconds; Entity = entity;
        }
        public string ProfileId { get; private set; }
        public int ProfileRevision { get; private set; }
        public int Seed { get; private set; }
        public long Generation { get; private set; }
        public long SnapshotSequence { get; private set; }
        public long Tick { get; private set; }
        public bool Paused { get; private set; }
        public RuntimeStatus Status { get; private set; }
        public string Failure { get; private set; }
        public double LastTickMilliseconds { get; private set; }
        public FoundationEntitySnapshot Entity { get; private set; }
    }
}

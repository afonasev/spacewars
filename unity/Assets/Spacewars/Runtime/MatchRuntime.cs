using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed class MatchRuntime : IDisposable
    {
        public const int MaxOutstandingCommands = 256;
        private const double TickSeconds = 1d / 30d;
        private readonly object gate = new object();
        private readonly Queue<GameCommand> inbox = new Queue<GameCommand>();
        private readonly Queue<CommandReceipt> receipts = new Queue<CommandReceipt>();
        private readonly Dictionary<string, long> lastSequenceByPlayer = new Dictionary<string, long>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread worker;
        private readonly FoundationProfile profile;
        private readonly long generation;
        private readonly int seed;
        private FoundationMatchState state = new FoundationMatchState();
        private FoundationSnapshot latest;
        private int pauseRequested;
        private int stopRequested;
        private int stopped;
        private int terminal;
        private int outstanding;
        private string failure;
        private long snapshotSequence;

        public MatchRuntime(FoundationProfile profile, long generation, int seed)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (generation < 1) throw new ArgumentOutOfRangeException("generation");
            this.profile = profile; this.generation = generation; this.seed = seed;
            latest = Snapshot(RuntimeStatus.Starting, false, 0d);
            worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Spacewars.Foundation.MatchRuntime" };
            worker.Start();
        }

        public FoundationSnapshot Latest { get { return Volatile.Read(ref latest); } }
        public long Generation { get { return generation; } }
        public int Seed { get { return seed; } }
        public bool IsStopped { get { return Volatile.Read(ref stopped) != 0; } }

        public CommandSubmitResult TrySubmit(GameCommand command)
        {
            if (command == null) return new CommandSubmitResult(CommandSubmitStatus.InvalidEntity);
            if (IsStopped) return new CommandSubmitResult(CommandSubmitStatus.Stopped);
            if (Volatile.Read(ref pauseRequested) != 0) return new CommandSubmitResult(CommandSubmitStatus.Paused);
            if (failure != null) return new CommandSubmitResult(CommandSubmitStatus.Failed);
            if (command.SchemaVersion != GameCommand.CurrentSchemaVersion) return new CommandSubmitResult(CommandSubmitStatus.InvalidSequence);
            if (command.Generation != generation) return new CommandSubmitResult(CommandSubmitStatus.StaleGeneration);
            if (command.Sequence < 1) return new CommandSubmitResult(CommandSubmitStatus.InvalidSequence);
            if (command.PlayerId != FoundationFixtureGeometry.PlayerId) return new CommandSubmitResult(CommandSubmitStatus.InvalidOwner);
            if (command.EntityId != FoundationFixtureGeometry.EntityId) return new CommandSubmitResult(CommandSubmitStatus.InvalidEntity);
            if (command.Type != FoundationCommandType.Move && command.Type != FoundationCommandType.Stop) return new CommandSubmitResult(CommandSubmitStatus.InvalidTarget);
            if (command.Type == FoundationCommandType.Move && !ValidTarget(command.TargetX, command.TargetZ)) return new CommandSubmitResult(CommandSubmitStatus.InvalidTarget);

            lock (gate)
            {
                if (Volatile.Read(ref stopRequested) != 0) return new CommandSubmitResult(CommandSubmitStatus.Stopped);
                if (Volatile.Read(ref terminal) != 0 || failure != null) return new CommandSubmitResult(failure == null ? CommandSubmitStatus.Stopped : CommandSubmitStatus.Failed);
                if (Volatile.Read(ref pauseRequested) != 0) return new CommandSubmitResult(CommandSubmitStatus.Paused);
                long previous;
                if (lastSequenceByPlayer.TryGetValue(command.PlayerId, out previous) && command.Sequence <= previous)
                    return new CommandSubmitResult(CommandSubmitStatus.InvalidSequence);
                if (outstanding >= MaxOutstandingCommands) return new CommandSubmitResult(CommandSubmitStatus.Overflow);
                lastSequenceByPlayer[command.PlayerId] = command.Sequence;
                inbox.Enqueue(command);
                outstanding++;
            }
            SignalWorker();
            return new CommandSubmitResult(CommandSubmitStatus.Accepted);
        }

        public IReadOnlyList<CommandReceipt> DrainReceipts()
        {
            lock (gate)
            {
                CommandReceipt[] drained = receipts.ToArray();
                receipts.Clear();
                outstanding -= drained.Length;
                return drained;
            }
        }

        public void RequestPause(bool paused)
        {
            if (IsStopped) return;
            Volatile.Write(ref pauseRequested, paused ? 1 : 0);
            SignalWorker();
        }

        public void RequestStop()
        {
            if (Interlocked.Exchange(ref stopRequested, 1) == 0) SignalWorker();
        }

        // Deliberately nonblocking: Unity's main thread polls IsStopped after RequestStop.
        public void Dispose() { RequestStop(); }

        private bool ValidTarget(double x, double z)
        {
            return !Double.IsNaN(x) && !Double.IsInfinity(x) && !Double.IsNaN(z) && !Double.IsInfinity(z)
                && x >= -profile.GroundHalfExtent && x <= profile.GroundHalfExtent
                && z >= -profile.GroundHalfExtent && z <= profile.GroundHalfExtent;
        }

        private void WorkerLoop()
        {
            try
            {
                Stopwatch clock = Stopwatch.StartNew();
                double nextTick = 0d;
                bool pauseBarrierApplied = false;
                while (Volatile.Read(ref stopRequested) == 0)
                {
                    double now = clock.Elapsed.TotalSeconds;
                    if (now < nextTick) { wake.WaitOne((int)Math.Min(20, Math.Ceiling((nextTick - now) * 1000d))); continue; }
                    bool paused = Volatile.Read(ref pauseRequested) != 0;
                    Stopwatch tickClock = Stopwatch.StartNew();
                    if (!paused)
                    {
                        pauseBarrierApplied = false;
                        Tick();
                    }
                    else if (!pauseBarrierApplied)
                    {
                        ApplyCommands(state.Tick);
                        pauseBarrierApplied = true;
                    }
                    tickClock.Stop();
                    Volatile.Write(ref latest, Snapshot(paused ? RuntimeStatus.Paused : RuntimeStatus.Running, paused, tickClock.Elapsed.TotalMilliseconds));
                    nextTick += TickSeconds;
                    // Avoid an unbounded catch-up loop after a stall while retaining one fixed simulation step per published tick.
                    if (nextTick < clock.Elapsed.TotalSeconds) nextTick = clock.Elapsed.TotalSeconds + TickSeconds;
                }
                Volatile.Write(ref latest, Snapshot(RuntimeStatus.Stopped, Volatile.Read(ref pauseRequested) != 0, 0d));
            }
            catch (Exception exception)
            {
                failure = exception.GetType().Name + ": " + exception.Message;
                Volatile.Write(ref latest, Snapshot(RuntimeStatus.Failed, false, 0d));
            }
            finally
            {
                Interlocked.Exchange(ref terminal, 1);
                lock (gate)
                {
                    while (inbox.Count > 0)
                    {
                        GameCommand command = inbox.Dequeue();
                        receipts.Enqueue(new CommandReceipt(command.Sequence, state.Tick, CommandReceiptStatus.Cancelled));
                    }
                }
                Volatile.Write(ref stopped, 1);
                wake.Dispose();
            }
        }

        private void Tick()
        {
            ApplyCommands(state.Tick + 1);
            lock (gate)
            {
                state.Advance(TickSeconds, profile.MoveSpeed);
                state.Tick++;
            }
        }

        private void ApplyCommands(long appliedTick)
        {
            lock (gate)
            {
                while (inbox.Count > 0)
                {
                    GameCommand command = inbox.Dequeue();
                    state.Apply(command);
                    receipts.Enqueue(new CommandReceipt(command.Sequence, appliedTick, CommandReceiptStatus.Applied));
                }
            }
        }

        private FoundationSnapshot Snapshot(RuntimeStatus status, bool paused, double milliseconds)
        {
            lock (gate)
            {
                snapshotSequence++;
                return new FoundationSnapshot(profile.ProfileId, profile.Revision, seed, generation, snapshotSequence, state.Tick, paused, status, failure, milliseconds,
                    new FoundationEntitySnapshot(FoundationFixtureGeometry.EntityId, state.X, state.Z, state.Moving));
            }
        }

        private void SignalWorker()
        {
            try { wake.Set(); }
            catch (ObjectDisposedException) { }
        }
    }
}

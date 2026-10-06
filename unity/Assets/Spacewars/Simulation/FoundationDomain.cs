using System;

namespace Spacewars.Simulation
{
    public enum FoundationCommandType { Move, Stop }
    public enum CommandSubmitStatus { Accepted, Stopped, Paused, StaleGeneration, InvalidSequence, InvalidOwner, InvalidEntity, InvalidTarget, Overflow, Failed }

    public sealed class GameCommand
    {
        public const int CurrentSchemaVersion = 1;
        public GameCommand(int schemaVersion, long generation, long sequence, string playerId, string entityId, FoundationCommandType type, double targetX, double targetZ)
        {
            SchemaVersion = schemaVersion; Generation = generation; Sequence = sequence; PlayerId = playerId; EntityId = entityId; Type = type; TargetX = targetX; TargetZ = targetZ;
        }
        public int SchemaVersion { get; private set; }
        public long Generation { get; private set; }
        public long Sequence { get; private set; }
        public string PlayerId { get; private set; }
        public string EntityId { get; private set; }
        public FoundationCommandType Type { get; private set; }
        public double TargetX { get; private set; }
        public double TargetZ { get; private set; }
        public static GameCommand Move(long generation, long sequence, string playerId, string entityId, double x, double z) { return new GameCommand(CurrentSchemaVersion, generation, sequence, playerId, entityId, FoundationCommandType.Move, x, z); }
        public static GameCommand Stop(long generation, long sequence, string playerId, string entityId) { return new GameCommand(CurrentSchemaVersion, generation, sequence, playerId, entityId, FoundationCommandType.Stop, 0d, 0d); }
    }

    public sealed class CommandSubmitResult
    {
        public CommandSubmitResult(CommandSubmitStatus status) { Status = status; }
        public CommandSubmitStatus Status { get; private set; }
        public bool Accepted { get { return Status == CommandSubmitStatus.Accepted; } }
    }

    public enum CommandReceiptStatus { Applied, Cancelled }

    public sealed class CommandReceipt
    {
        public CommandReceipt(long sequence, long appliedTick, CommandReceiptStatus status) { Sequence = sequence; AppliedTick = appliedTick; Status = status; }
        public long Sequence { get; private set; }
        public long AppliedTick { get; private set; }
        public CommandReceiptStatus Status { get; private set; }
        public override string ToString() { return "command " + Sequence + " " + Status + " at tick " + AppliedTick; }
    }

    internal sealed class FoundationMatchState
    {
        public double X = FoundationFixtureGeometry.InitialX;
        public double Z = FoundationFixtureGeometry.InitialZ;
        public bool Moving;
        public double TargetX;
        public double TargetZ;
        public long Tick;

        public void Apply(GameCommand command)
        {
            if (command.Type == FoundationCommandType.Stop) { Moving = false; return; }
            TargetX = command.TargetX; TargetZ = command.TargetZ; Moving = true;
        }

        public void Advance(double deltaSeconds, double speed)
        {
            if (!Moving) return;
            FoundationDiagnosticMovement.Step(ref X, ref Z, ref Moving, TargetX, TargetZ, deltaSeconds, speed);
        }
    }

    public static class FoundationDiagnosticMovement
    {
        public static void Step(ref double x, ref double z, ref bool moving, double targetX, double targetZ, double deltaSeconds, double speed)
        {
            if (!moving) return;
            double dx = targetX - x, dz = targetZ - z, distance = Math.Sqrt(dx * dx + dz * dz), step = speed * deltaSeconds;
            if (distance <= step || distance == 0d) { x = targetX; z = targetZ; moving = false; return; }
            x += dx / distance * step; z += dz / distance * step;
        }
    }
}

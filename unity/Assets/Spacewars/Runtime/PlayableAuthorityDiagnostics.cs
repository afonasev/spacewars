using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class PlayableAuthorityTick
    {
        // Trusted local diagnostic host only; private AI checkpoints must never enter network projection.
        public PlayableAiOwnerCheckpoint[] CaptureDiagnosticCheckpoints()=>scheduler.Owners.Select(o=>o.Checkpoint).ToArray();
        public PlayableSnapshot ParticipantView(string id)=>domain.PlayerSnapshot(sequence,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed,domain.OwnerFor(id));
        // Detached live ledger, not the older policy-observation checkpoint. Read-only diagnostics.
        public AiBudgetState[] CaptureDiagnosticBudgets()=>scheduler.Owners.Select(o=>o.Budget.Capture()).ToArray();
        public bool DiagnosticFactoryExitUsable(int id,PlayableEntityKind kind)=>domain.DiagnosticFactoryExitUsable(id,kind);
        public int? WinnerTeam=>domain.WinnerTeam;
    }
    internal sealed partial class PlayableDomain
    {
        internal bool DiagnosticFactoryExitUsable(int id,PlayableEntityKind kind)=>buildings.TryGetValue(id,out var b)&&b.Kind==PlayableBuildingKind.Factory&&PlayableUnitRules.Supported(kind)&&TryFactoryExit(b,kind,out _);
    }
}

using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    // One seat's most recent click. Projection may split that click into commands
    // with different sequences and assigned destinations. Keep the original point.
    internal sealed class PlayerOrderRepeatFilter
    {
        private readonly HashSet<long> awaitingReceipt = new HashSet<long>();
        private readonly HashSet<long> applied = new HashSet<long>();
        private PlayableCommand last;
        private string surface;
        private int relay;
        private bool projecting;
        private long firstFreshSnapshot;

        public void Clear()
        {
            last = null;
            projecting = false;
            awaitingReceipt.Clear();
            applied.Clear();
            firstFreshSnapshot = -1;
        }

        public void SelectionChanged(IEnumerable<int> ids)
        {
            if (last != null && !new HashSet<int>(last.EntityIds).SetEquals(ids)) Clear();
        }

        public void CancelRecipients(PlayableCommand stop)
        {
            if (last != null && last.EntityIds.Intersect(stop.EntityIds).Any()) Clear();
        }

        public bool IsRepeat(PlayableSnapshot view, PlayableCommand command, int relayId, string clickedSurface)
        {
            if (last == null || last.Generation != command.Generation || last.PlayerId != command.PlayerId ||
                last.Kind != command.Kind || last.Mode != command.Mode || relay != relayId ||
                !new HashSet<int>(last.EntityIds).SetEquals(command.EntityIds)) return false;

            if (last.TargetId != 0 || command.TargetId != 0)
            {
                if (last.TargetId != command.TargetId) return false;
            }
            else
            {
                double dx = command.Target.X - last.Target.X, dz = command.Target.Z - last.Target.Z;
                if (surface != clickedSurface || !(dx * dx + dz * dz < 1)) return false;
            }
            if (projecting || awaitingReceipt.Count != 0) return true;
            if (applied.Count == 0 || view == null) return false;
            if (view.Sequence < firstFreshSnapshot) return true;
            foreach (var unit in view.Entities)
            {
                if (unit.Owner != view.Owner || !last.EntityIds.Contains(unit.Id)) continue;
                var queue = unit.Queue;
                if (last.Mode == PlayableOrderMode.Append && queue?.Deferred.Count > 0)
                {
                    if (applied.Contains(queue.Deferred[queue.Deferred.Count - 1].CommandSequence)) return true;
                    continue;
                }
                var sequence = queue?.Pending?.CommandSequence ?? queue?.Active?.CommandSequence ??
                    unit.CurrentOrder?.CommandSequence;
                if (sequence.HasValue && applied.Contains(sequence.Value)) return true;
            }
            return false;
        }

        public void Enqueued(PlayableCommand intent, int relayId, string clickedSurface)
        {
            switch (intent.Kind)
            {
                case PlayableCommandKind.Move:
                case PlayableCommandKind.AttackMove:
                case PlayableCommandKind.Attack:
                case PlayableCommandKind.Follow:
                case PlayableCommandKind.RepairTarget:
                    Clear();
                    last = intent;
                    projecting = true;
                    relay = relayId;
                    surface = clickedSurface;
                    break;
            }
        }

        public void ProjectionFinished(PlayableCommand intent)
        {
            if (ReferenceEquals(last, intent)) projecting = false;
        }

        public void Submitted(PlayableCommand intent, PlayableCommand command)
        {
            if (ReferenceEquals(last, intent)) awaitingReceipt.Add(command.Sequence);
        }

        public void Observe(PlayableSnapshot view, IEnumerable<PlayableCommandReceipt> receipts)
        {
            if (last == null || view == null) return;
            if (view.Generation != last.Generation || view.OwnerId != last.PlayerId) { Clear(); return; }
            foreach (var receipt in receipts)
            {
                if (receipt.OwnerId != last.PlayerId || !awaitingReceipt.Remove(receipt.Sequence)) continue;
                if (receipt.Status == PlayableCommandStatus.Applied)
                {
                    applied.Add(receipt.Sequence);
                    firstFreshSnapshot = view.Sequence + 1;
                }
            }
        }
    }
}

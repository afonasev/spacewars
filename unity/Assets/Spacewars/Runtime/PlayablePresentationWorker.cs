using System;
using System.Collections.Generic;
using System.Threading;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class PlayableRuntime
    {
        // Inputs are detached from simulation. Unfiltered trail samples never enter
        // a player snapshot or the public frame until tested against that owner's fog.
        internal sealed class PresentationInput
        {
            internal readonly PlayableSnapshot[] Players;
            internal readonly PlayableSnapshot Overview;
            internal readonly PlayableGroundTrail[] Trails;
            internal readonly PlayableOwner? CompletedOwner;
            internal readonly OfflineReceipt[] Receipts;
            internal readonly long ReceiptOrdinal;
            internal readonly PlayableSnapshot Primary;

            internal PresentationInput(PlayableSnapshot[] players, PlayableSnapshot overview,
                PlayableGroundTrail[] trails, PlayableOwner? completedOwner, OfflineReceipt[] receipts, long ordinal, PlayableSnapshot primary = null)
            {
                Players = players;
                Overview = overview;
                Trails = trails;
                CompletedOwner = completedOwner;
                Receipts = receipts;
                ReceiptOrdinal = ordinal;
                Primary = primary;
            }
        }

        internal sealed class PresentationFrames
        {
            internal readonly PlayableSpectatorFrame Spectator;
            internal readonly OfflinePresentationFrame Offline;

            private static void CompleteRows(PlayableSnapshot view)
            {
                if (view == null) return;
                _ = view.Entities.Count;
                _ = view.Buildings.Count;
            }

            internal PresentationFrames(PresentationInput input, PresentationFrames previous)
            {
                CompleteRows(input.Primary);
                CompleteRows(input.Overview);
                var players = new PlayableSnapshot[input.Players.Length];
                var filtered = new Dictionary<TeamVisionSnapshot, PlayableGroundTrail[]>();
                for (int i = 0; i < players.Length; i++) {
                    var view = input.Players[i];
                    CompleteRows(view);
                    if (view.Owner != input.CompletedOwner && input.Trails.Length > 0) {
                        if (!filtered.TryGetValue(view.Vision, out var visible)) {
                            visible = GroundTrailJournal.FilterVisible(input.Trails, view.Vision);
                            filtered.Add(view.Vision, visible);
                        }
                        view = view.WithFrameTrails(visible);
                    }
                    players[i] = view;
                }
                if (input.Overview != null) {
                    var current = new PlayableSpectatorFrame(input.Overview.WithFrameTrails(input.Trails), players);
                    Spectator = new PlayableSpectatorFrame(current, previous?.Spectator);
                }
                if (input.Receipts != null) {
                    var views = new Dictionary<string, PlayableSnapshot>(players.Length);
                    foreach (var player in players) views.Add(player.OwnerId, player);
                    Offline = new OfflinePresentationFrame(views, input.Receipts, input.ReceiptOrdinal, previous?.Offline);
                }
            }
        }

        private void CompletePresentation(PresentationInput input)
        {
            var frames = new PresentationFrames(input, Volatile.Read(ref publishedViews));
            Volatile.Write(ref publishedViews, frames);
        }

        // A slow renderer never holds the simulation lock. Keep the in-flight
        // publication and only the newest pending input; the authoritative receipt
        // history and match journal are recorded independently on the simulation.
        internal sealed class PresentationWorker : IDisposable
        {
            private readonly object sync = new object();
            private readonly Action<PresentationInput> publish;
            private readonly Thread thread;
            private PresentationInput pending;
            private Exception failure;
            private bool stopping;

            internal PresentationWorker(Action<PresentationInput> publish)
            {
                this.publish = publish;
                thread = new Thread(Run) { IsBackground = true, Name = "Spacewars.PresentationFrames" };
                thread.Start();
            }

            internal void Submit(PresentationInput input)
            {
                lock (sync) {
                    CheckFailure();
                    if (stopping) throw new ObjectDisposedException(nameof(PresentationWorker));
                    pending = input;
                    Monitor.Pulse(sync);
                }
            }

            private void Run()
            {
                try {
                    while (true) {
                        PresentationInput next;
                        lock (sync) {
                            while (pending == null && !stopping) Monitor.Wait(sync);
                            if (pending == null) return;
                            next = pending;
                            pending = null;
                        }
                        publish(next);
                    }
                } catch (Exception error) {
                    lock (sync) { failure = error; pending = null; }
                }
            }

            private void CheckFailure()
            {
                if (failure != null) throw new InvalidOperationException("Presentation frame failed.", failure);
            }

            public void Dispose()
            {
                lock (sync) { stopping = true; Monitor.Pulse(sync); }
                thread.Join();
                CheckFailure();
            }
        }
    }
}

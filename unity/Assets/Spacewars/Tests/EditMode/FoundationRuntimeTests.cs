using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class FoundationRuntimeTests
    {
        [Test]
        public void ProfileRejectsNonFiniteAndOutOfRangeValues()
        {
            FoundationProfileData invalid = ProfileData();
            invalid.moveSpeed = Double.NaN;
            Assert.Throws<ArgumentException>(() => FoundationProfile.Create(invalid));
            invalid = ProfileData();
            invalid.cameraZoom = 31d;
            Assert.Throws<ArgumentException>(() => FoundationProfile.Create(invalid));
        }

        [Test]
        public void DiagnosticMovementIsDeterministic()
        {
            double ax = 0d, az = 0d, bx = 0d, bz = 0d;
            bool am = true, bm = true;
            for (int index = 0; index < 30; index++)
            {
                FoundationDiagnosticMovement.Step(ref ax, ref az, ref am, 4d, 3d, 1d / 30d, 4d);
                FoundationDiagnosticMovement.Step(ref bx, ref bz, ref bm, 4d, 3d, 1d / 30d, 4d);
            }
            Assert.AreEqual(ax, bx); Assert.AreEqual(az, bz); Assert.AreEqual(am, bm);
        }

        [Test]
        public void OutstandingCommandsAreBoundedUntilReceiptsAreDrained()
        {
            MatchRuntime runtime = Runtime(1);
            try
            {
                for (int sequence = 1; sequence <= MatchRuntime.MaxOutstandingCommands; sequence++)
                    Assert.IsTrue(runtime.TrySubmit(GameCommand.Stop(1, sequence, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId)).Accepted);
                Assert.AreEqual(CommandSubmitStatus.Overflow, runtime.TrySubmit(GameCommand.Stop(1, 257, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId)).Status);
                long submittedAtTick = runtime.Latest.Tick;
                WaitUntil(() => runtime.Latest.Tick >= submittedAtTick + 2);
                Assert.AreEqual(MatchRuntime.MaxOutstandingCommands, runtime.DrainReceipts().Count);
                Assert.IsTrue(runtime.TrySubmit(GameCommand.Stop(1, 257, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId)).Accepted);
            }
            finally { Stop(runtime); }
        }

        [Test]
        public void PublishedSnapshotsAreImmutableAndWorkerDoesNotNeedAConsumer()
        {
            MatchRuntime runtime = Runtime(1);
            try
            {
                FoundationSnapshot before = runtime.Latest;
                WaitUntil(() => runtime.Latest.Tick >= before.Tick + 2);
                Assert.Greater(runtime.Latest.Tick, before.Tick);
                Assert.IsTrue(runtime.TrySubmit(GameCommand.Move(1, 1, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId, 4d, 0d)).Accepted);
                WaitUntil(() => runtime.Latest.Entity.X > 0d);
                Assert.AreEqual(0d, before.Entity.X);
                Assert.IsFalse(before.Entity.Moving);
            }
            finally { Stop(runtime); }
        }

        [Test]
        public void CommandsValidateSchemaOwnershipCoordinatesAndSequenceAndReceiptsStayOrdered()
        {
            MatchRuntime runtime = Runtime(1);
            try
            {
                Assert.AreEqual(CommandSubmitStatus.InvalidSequence, runtime.TrySubmit(new GameCommand(99, 1, 1, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId, FoundationCommandType.Stop, 0d, 0d)).Status);
                Assert.AreEqual(CommandSubmitStatus.InvalidOwner, runtime.TrySubmit(GameCommand.Stop(1, 1, "other-player", FoundationFixtureGeometry.EntityId)).Status);
                Assert.AreEqual(CommandSubmitStatus.InvalidEntity, runtime.TrySubmit(GameCommand.Stop(1, 1, FoundationFixtureGeometry.PlayerId, "other-tank")).Status);
                Assert.AreEqual(CommandSubmitStatus.InvalidTarget, runtime.TrySubmit(GameCommand.Move(1, 1, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId, Double.NaN, 0d)).Status);
                Assert.AreEqual(CommandSubmitStatus.InvalidTarget, runtime.TrySubmit(GameCommand.Move(1, 1, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId, 21d, 0d)).Status);
                Assert.IsTrue(runtime.TrySubmit(GameCommand.Move(1, 1, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId, 2d, 0d)).Accepted);
                Assert.IsTrue(runtime.TrySubmit(GameCommand.Stop(1, 2, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId)).Accepted);
                Assert.AreEqual(CommandSubmitStatus.InvalidSequence, runtime.TrySubmit(GameCommand.Stop(1, 2, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId)).Status);
                long submittedAtTick = runtime.Latest.Tick;
                WaitUntil(() => runtime.Latest.Tick >= submittedAtTick + 2);
                var receipts = runtime.DrainReceipts();
                Assert.AreEqual(2, receipts.Count);
                Assert.AreEqual(1, receipts[0].Sequence); Assert.AreEqual(CommandReceiptStatus.Applied, receipts[0].Status);
                Assert.AreEqual(2, receipts[1].Sequence); Assert.AreEqual(CommandReceiptStatus.Applied, receipts[1].Status);
            }
            finally { Stop(runtime); }
        }

        [Test]
        public void StopCancelsQueuedCommandsAndPreservesBoundedReceipts()
        {
            MatchRuntime runtime = Runtime(1);
            try
            {
                for (int sequence = 1; sequence <= MatchRuntime.MaxOutstandingCommands; sequence++)
                    Assert.IsTrue(runtime.TrySubmit(GameCommand.Stop(1, sequence, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId)).Accepted);
                runtime.RequestStop();
                WaitUntil(() => runtime.IsStopped);
                var receipts = runtime.DrainReceipts();
                Assert.AreEqual(MatchRuntime.MaxOutstandingCommands, receipts.Count);
                Assert.IsTrue(Array.Exists(ToArray(receipts), receipt => receipt.Status == CommandReceiptStatus.Cancelled));
            }
            finally { if (!runtime.IsStopped) Stop(runtime); }
        }

        [Test]
        public void PauseGatesInputAndStaleGenerationAndDisposeStopWithoutMainThreadJoin()
        {
            MatchRuntime runtime = Runtime(2);
            try
            {
                Assert.AreEqual(CommandSubmitStatus.StaleGeneration, runtime.TrySubmit(GameCommand.Stop(1, 1, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId)).Status);
                runtime.RequestPause(true);
                Assert.AreEqual(CommandSubmitStatus.Paused, runtime.TrySubmit(GameCommand.Stop(2, 1, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId)).Status);
                WaitUntil(() => runtime.Latest.Paused);
                FoundationSnapshot paused = runtime.Latest;
                Thread.Sleep(90);
                Assert.AreEqual(paused.Tick, runtime.Latest.Tick);
                Assert.AreEqual(paused.Entity.X, runtime.Latest.Entity.X);
                runtime.RequestPause(false);
                WaitUntil(() => !runtime.Latest.Paused && runtime.Latest.Status == RuntimeStatus.Running);
                Stopwatch stopwatch = Stopwatch.StartNew();
                runtime.Dispose();
                stopwatch.Stop();
                Assert.Less(stopwatch.ElapsedMilliseconds, 100L);
                WaitUntil(() => runtime.IsStopped);
            }
            finally { if (!runtime.IsStopped) Stop(runtime); }
        }

        [Test]
        public void PauseBoundaryAcknowledgesPreviouslyAcceptedCommandWithoutAdvancingPausedState()
        {
            MatchRuntime runtime = Runtime(1);
            try
            {
                Assert.IsTrue(runtime.TrySubmit(GameCommand.Move(1, 1, FoundationFixtureGeometry.PlayerId, FoundationFixtureGeometry.EntityId, 8d, 0d)).Accepted);
                runtime.RequestPause(true);
                WaitUntil(() => runtime.Latest.Paused);
                FoundationSnapshot acknowledgedPause = runtime.Latest;
                var receipts = runtime.DrainReceipts();
                Assert.AreEqual(1, receipts.Count);
                Assert.AreEqual(1, receipts[0].Sequence);
                Assert.AreEqual(CommandReceiptStatus.Applied, receipts[0].Status);
                Assert.LessOrEqual(receipts[0].AppliedTick, acknowledgedPause.Tick);
                Thread.Sleep(90);
                Assert.AreEqual(acknowledgedPause.Tick, runtime.Latest.Tick);
                Assert.AreEqual(acknowledgedPause.Entity.X, runtime.Latest.Entity.X);
                Assert.AreEqual(acknowledgedPause.Entity.Z, runtime.Latest.Entity.Z);
            }
            finally { Stop(runtime); }
        }

        private static MatchRuntime Runtime(long generation) { return new MatchRuntime(FoundationProfile.Create(ProfileData()), generation, 7); }

        private static FoundationProfileData ProfileData()
        {
            return new FoundationProfileData
            {
                schemaVersion = 1, profileId = "unity-foundation-diagnostic-v1", revision = 1,
                sourceCommit = "ab1d42d5b1e8af104509efb6d204a4c310be00f1", sourceProfileId = "local:main:1788708872201:1",
                sourceProfileRevision = 5, sourceManifestSha256 = "af6c3085e1a487c827ea104b86242c3931b46ac1f6756fa74d6c1cddbdaf09fb",
                cameraPanSpeed = 8d, cameraPitchDegrees = 55d, cameraHeight = 28d, cameraOffsetZ = -20d,
                cameraZoom = 12d, cameraMinZoom = 6d, cameraMaxZoom = 30d, cameraZoomSpeed = .01d, moveSpeed = 4d, groundHalfExtent = 20d,
            };
        }

        private static void Stop(MatchRuntime runtime) { runtime.RequestStop(); WaitUntil(() => runtime.IsStopped); }
        private static CommandReceipt[] ToArray(System.Collections.Generic.IReadOnlyList<CommandReceipt> receipts)
        {
            var copy = new CommandReceipt[receipts.Count];
            for (int index = 0; index < receipts.Count; index++) copy[index] = receipts[index];
            return copy;
        }
        private static void WaitUntil(Func<bool> predicate)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!predicate())
            {
                if (stopwatch.ElapsedMilliseconds > 1500) Assert.Fail("Timed out waiting for MatchRuntime.");
                Thread.Sleep(2);
            }
        }
    }
}

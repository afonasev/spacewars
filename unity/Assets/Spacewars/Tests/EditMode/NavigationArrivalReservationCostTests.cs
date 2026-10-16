using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        // Bounded source-specific comparison, never a replacement for AllNth.
        // Every case performs the original complete count pass, then one actual
        // refusal with the original input, Advance grant and quota.
        [TestCase(1)] [TestCase(66556)] [TestCase(133111)]
        public void ArrivalReservationCostPreservesFullCountAndBoundaryCleanup(int nth)
        {
            using (var fixture = new OwnedArrivalFixture(false))
            {
                long inputBytes = Unique(fixture.Input.StorageRoot);
                int inputReferences = fixture.Input.StorageRoot.References;
                Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 2000, out var countScope));
                var counter = new Refusal(countScope, int.MaxValue);
                NavigationArrivalWork work = null;
                var clock = Stopwatch.StartNew();
                try
                {
                    work = NavigationArrivalWork.CreateOwned(fixture.Input, counter);
                    work.Advance(1, 10000, ArrivalWorkQuota);
                    work.SealPrepared(); countScope.Attach(work);
                }
                finally { work?.ReleaseOwner(); countScope.Discard(); }
                double countMs = clock.Elapsed.TotalMilliseconds;
                Assert.AreEqual(ArrivalAdvanceReservationCount, counter.Calls);
                Assert.AreEqual(inputBytes, fixture.Budget.PhysicalBytes);
                Assert.Zero(fixture.Budget.HeldBytes);
                Assert.AreEqual(inputReferences, fixture.Input.StorageRoot.References);

                Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 2000 + nth, out var scope));
                var refusal = new Refusal(scope, nth);
                work = null;
                bool refused = false;
                clock.Restart();
                try
                {
                    work = NavigationArrivalWork.CreateOwned(fixture.Input, refusal);
                    work.Advance(1, 10000, ArrivalWorkQuota);
                    scope.Attach(work);
                }
                catch (InvalidOperationException) { refused = true; }
                finally { work?.ReleaseOwner(); scope.Discard(); }
                double refusalMs = clock.Elapsed.TotalMilliseconds;
                Assert.True(refused);
                Assert.AreEqual(nth, refusal.Calls);
                Assert.AreEqual(inputBytes, fixture.Budget.PhysicalBytes);
                Assert.Zero(fixture.Budget.HeldBytes);
                Assert.AreEqual(inputReferences, fixture.Input.StorageRoot.References);
                fixture.ReleaseInput();
                Assert.Zero(fixture.Budget.PhysicalBytes);
                Assert.Zero(fixture.Budget.HeldBytes);

                var path = Environment.GetEnvironmentVariable("SPACEWARS_RESERVATION_COST_PATH");
                if (!string.IsNullOrEmpty(path))
                    File.AppendAllText(path, string.Format(CultureInfo.InvariantCulture,
                        "nth={0}|count={1}|count_and_cleanup_ms={2:F3}|refusal_and_cleanup_ms={3:F3}|assertions=passed\n",
                        nth, counter.Calls, countMs, refusalMs));
            }
        }
    }
}

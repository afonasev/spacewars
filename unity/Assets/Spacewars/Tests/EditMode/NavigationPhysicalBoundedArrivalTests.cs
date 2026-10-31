using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private sealed class BoundedLazyArrivalFixture : IDisposable
        {
            internal readonly NavigationRetentionBudget Budget = new NavigationRetentionBudget();
            internal readonly NavArrivalInput Input;

            internal BoundedLazyArrivalFixture(bool bounded)
            {
                var raw = LazyRequest();
                var members = new[] {
                    NavigationArrivalTestFactory.Member(31, 0, raw.Subscription.Origin),
                    NavigationArrivalTestFactory.Member(32, 0, raw.Subscription.Origin)
                };
                var arrival = new NavigationProfile("bounded-lazy-arrival", 1, .25, 4, 4.5, 1, .15, 3.6, .08,
                    groupCorridorMaximumExcessPercent: bounded ? 15 : 0);
                var source = new NavArrivalInput("topology:" + raw.Provider.Binding, "profile:" + raw.Provider.Binding,
                    raw.Provider, raw.Profile, arrival, raw.Limits,
                    new NavArrivalCandidateSource("bounded-lazy-target", 1, raw.Region.Requested, raw.Region.Candidates),
                    members, NavigationArrivalTestFactory.Occupancy(), raw.MaxLabels, null, true);
                Assert.True(Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 1, out var birth));
                using (birth) {
                    var reservation = birth.OwnedReservation();
                    var provider = PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)source.Provider, reservation);
                    var metadata = NavGraphMetadata.CopyOptionalUnowned(members[0].Clearance, source.Profile, source.Limits, reservation);
                    var profile = NavigationProfile.CopyOwned(arrival, reservation);
                    var candidates = NavArrivalCandidateSource.CopyOwned(source.Source, reservation);
                    var occupancy = NavArrivalOccupancySnapshot.CopyOwned(source.Occupancy, Array.Empty<NavArrivalOccupant>(), reservation);
                    var ownedMembers = members.Select(m => NavArrivalMember.CopyOwned(m, metadata.Clearance, reservation)).ToArray();
                    var bindings = ownedMembers.Select(m => NavArrivalWorkingBinding.CreateOwned(m.EntityId, provider, metadata.Profile, reservation)).ToArray();
                    Input = NavArrivalInput.CopyOwned(source, provider, metadata.Profile, profile, metadata.Limits,
                        candidates, ownedMembers, occupancy, bindings, reservation);
                    provider.StorageRoot.ReleaseOwner(); metadata.Release(); profile.StorageRoot.ReleaseOwner();
                    candidates.StorageRoot.ReleaseOwner(); occupancy.StorageRoot.ReleaseOwner();
                    foreach (var member in ownedMembers) member.StorageRoot.ReleaseOwner();
                    foreach (var binding in bindings) binding.StorageRoot.ReleaseOwner();
                    birth.Attach(Input.StorageRoot); birth.Commit(Input.StorageRoot, _ => { });
                }
            }

            internal NavigationArrivalWork Partial()
            {
                Assert.True(Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 2, out var birth));
                NavigationArrivalWork work = null;
                try {
                    work = NavigationArrivalWork.CreateOwned(Input, birth.OwnedReservation());
                    PrepareArrivalModeRows(work);
                    Assert.IsNotEmpty(ArrivalModeRequests(work), "real CreateRequest rows, not synthetic mode flags");
                    work.SealPrepared(); birth.Attach(work); birth.Commit(work, _ => { });
                    return work;
                } catch { work?.ReleaseOwner(); birth.Discard(); throw; }
            }

            public void Dispose()
            {
                Input.StorageRoot.ReleaseOwner();
                Assert.Zero(Budget.PhysicalBytes); Assert.Zero(Budget.HeldBytes); Assert.Zero(Budget.ActiveScopes);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void OwnedBoundedLazyEligibleArrivalPhysicalRoundTrip(bool bounded)
        {
            using (var fixture = new BoundedLazyArrivalFixture(bounded)) {
                var work = fixture.Partial(); NavigationArrivalWork restored = null;
                var cold = new NavigationRetentionBudget();
                try {
                    foreach (var request in ArrivalModeRequests(work)) {
                        Assert.True(request.Query); Assert.True(request.GroupQuery);
                        Assert.True(NavDemandQueryWork.LazyEligible(request)); Assert.True(NavDemandQueryWork.CertifiedEligible(request));
                        Assert.AreEqual(bounded, request.CertifiedGroupCorridor);
                        Assert.AreEqual(bounded, request.CertifiedLazyPartition);
                        TestContext.WriteLine("ARRIVAL_REQUEST member=" + request.Subscription.EntityId + " maxLabels=" + request.MaxLabels +
                            " lazyEligible=True certifiedEligible=True bounded=" + request.CertifiedGroupCorridor +
                            " certified=" + request.CertifiedLazyPartition + " lazy=" + request.LazyPartition +
                            " origin=" + request.Subscription.Origin.Position + " endpoint=" + request.Subscription.AssignedEndpoint.Position +
                            " phase=" + CurrentArrivalPhase(work));
                    }
                    byte[] bytes = CaptureGraph(fixture.Budget, work);
                    byte[] Next(NavigationRetentionBudget budget, NavigationArrivalWork source) {
                        var next = Fork(source, budget, 99, w => w.Advance(source.LogicalEpoch + 1, 1, new NavEpochQuota(1, 1, 1, 1, 1)));
                        try { return CaptureGraph(budget, next); } finally { next.ReleaseOwner(); }
                    }
                    var expected = Next(fixture.Budget, work);
                    Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode, 1, 3, out var decode));
                    using (decode) { restored = (NavigationArrivalWork)Decode(bytes, decode.OwnedReservation()); decode.Attach(restored); decode.Commit(restored, _ => { }); }
                    CollectionAssert.AreEqual(bytes, CaptureGraph(cold, restored));
                    CollectionAssert.AreEqual(expected, Next(cold, restored));
                    CollectionAssert.AreEqual(bytes, CaptureGraph(fixture.Budget, work));
                } finally {
                    restored?.ReleaseOwner(); work.ReleaseOwner();
                    Assert.Zero(cold.PhysicalBytes); Assert.Zero(cold.HeldBytes); Assert.Zero(cold.ActiveScopes);
                }
            }
        }

        [TestCase((byte)2)] [TestCase((byte)8)]
        public void OwnedBoundedArrivalRejectsCorruptedRequestMode(byte corruptedMode)
        {
            using (var fixture = new BoundedLazyArrivalFixture(true)) {
                var work = fixture.Partial();
                try {
                    var request = ArrivalModeRequests(work).First(); Assert.True(request.CertifiedGroupCorridor);
                    var mode = typeof(NavScheduledRequest).GetField("exactMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    byte original = (byte)mode.GetValue(request);
                    try { mode.SetValue(request, corruptedMode); Assert.Throws<ArgumentException>(() => CaptureGraph(fixture.Budget, work)); }
                    finally { mode.SetValue(request, original); }
                } finally { work.ReleaseOwner(); }
            }
        }
    }
}

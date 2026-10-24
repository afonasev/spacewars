using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationArrivalGroupLifecycleTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private static byte[] Capture(NavigationSession nav)
        { using (var state = nav.CaptureOwnedState()) return WorldWire.Pack(writer => WorldWire.Write(writer, state)); }

        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(true, false)]
        public void ControlStopAdmissionKeepsArrivalOwnershipAtomic(bool follow, bool refuse)
        {
            var type = typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain", true);
            var domain = Activator.CreateInstance(type, Hidden, null,
                new object[] { PlayableProfile.Default, 1L, false }, null);
            object Call(string name, params object[] args) => type.GetMethod(name, Hidden).Invoke(domain, args);
            var nav = (NavigationSession)type.GetProperty("Navigation", Hidden).GetValue(domain);
            try
            {
                int first = (int)Call("SpawnUnit", new NavPoint(-12, -12), PlayableOwner.Player, PlayableEntityKind.Tank);
                int second = (int)Call("SpawnUnit", new NavPoint(-12, -9), PlayableOwner.Player, PlayableEntityKind.Tank);
                int target = (int)Call("SpawnUnit", new NavPoint(-6, -12),
                    follow ? PlayableOwner.Player : PlayableOwner.Enemy, PlayableEntityKind.Tank);
                // Initialize the actual physical host before the pending command;
                // capturing after the mutation would refresh group observations.
                using (nav.CaptureOwnedState()) { }
                var ids = new[] { first, second };
                var move = new PlayableCommand(1, 1, "player-1", PlayableCommandKind.Move,
                    ids, new NavPoint(-10, -12)).AsHuman();
                Assert.AreEqual(PlayableCommandStatus.Applied, Call("Apply", move, null));
                Assert.AreEqual(2, nav.AllocationCount);
                long group = nav.GroupFor(first).GroupId;
                var requests = ids.Select(id => nav.PendingRequestFor(id).Request).ToArray();
                var before = Capture(nav);
                int refused = 0;
                nav.PhysicalHostBudget.AllocationAdmission = _ =>
                {
                    if (!refuse || !new StackTrace().GetFrames().Any(f => f.GetMethod().Name == "TryOwnedStopGroup")) return true;
                    refused++;
                    return false;
                };
                object status;
                try
                {
                    status = Call("Apply", new PlayableCommand(1, 2, "player-1",
                        follow ? PlayableCommandKind.Follow : PlayableCommandKind.Attack, ids, targetId: target).AsHuman(), null);
                }
                finally { nav.PhysicalHostBudget.AllocationAdmission = null; }
                if (refuse) Assert.Greater(refused, 0, "The real owned Stop admission must refuse.");
                TestContext.WriteLine("ARRIVAL_GROUP_REFUSAL status=" + status + " oldGroup=" + group +
                    " newGroup=" + nav.GroupFor(first).GroupId + " allocations=" + nav.AllocationCount);
                if (refuse) CollectionAssert.AreEqual(before, Capture(nav), "Rejected control preserves the complete navigation state.");
                Assert.DoesNotThrow(() => nav.PreparePhysicalArrivalAttempt(),
                    "A refused control command must not orphan an accepted arrival behind a removed group.");
                Assert.AreEqual(refuse ? PlayableCommandStatus.Overflow : PlayableCommandStatus.Applied, status);
                if (refuse)
                {
                    foreach (int id in ids) Assert.AreEqual(group, nav.GroupFor(id).GroupId);
                    CollectionAssert.AreEqual(requests, ids.Select(id => nav.PendingRequestFor(id).Request).ToArray());
                }
                else
                {
                    Assert.Zero(nav.AllocationCount);
                    foreach (int id in ids)
                    {
                        Assert.False(nav.IsPending(id));
                        Assert.AreEqual(NavigationOutcome.Stopped, nav.Crowd.Units.Single(u => u.Id == id).Outcome);
                        Assert.AreEqual(follow ? PlayableCommandKind.Follow : PlayableCommandKind.Attack, nav.GroupFor(id).Kind);
                        Assert.AreEqual(2, nav.GroupFor(id).Members.Length);
                    }
                }
            }
            finally
            {
                nav.PhysicalHostBudget.AllocationAdmission = null;
                nav.ReleaseCommittedHostRoot();
                nav.DestroyNavigationAuthority();
                Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);
                Assert.Zero(nav.PhysicalHostBudget.HeldBytes);
                Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
    }
}

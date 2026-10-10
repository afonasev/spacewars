using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiTestMapTests
    {
        private static PlayableProfile Profile() => PlayableProfile.Create(PlayableProfile.Default.CopyData(), new AiTestMap());
        [Test] public void AllStartsSitesSlotsAndRoutesAreSupportedWithoutWalls()
        {
            var p = Profile(); var map = (AiTestMap)p.AuthoredMap;
            var roster = Enumerable.Range(0, 8).Select(i => new OfflineParticipant("ai-" + i, i + 1, i + 1, OfflineControl.Ai)).ToArray();
            var c = map.Configuration(p, 7108, roster); // Executes the production envelope/anchor/binding validator.
            Assert.AreEqual(8, c.Starts.Count); Assert.AreEqual(24, c.Sites.Count);
            Assert.AreEqual(12, c.Sites.Count(s => s.Kind == PlayableBuildingKind.Mine));
            Assert.AreEqual(4, c.Sites.Count(s => s.Kind == PlayableBuildingKind.Outpost));
            Assert.IsEmpty(c.Obstacles); Assert.IsEmpty(map.Solids); Assert.AreEqual(1, map.Supports.Count);
            Assert.IsEmpty(map.SurfaceTransitions);
            foreach (var site in c.Sites)
            {
                Assert.True(map.SupportsFootprint(site.Position, TerritoryRules.Radius(p, site.Kind)));
                foreach (var slot in site.Slots) Assert.True(map.SupportsFootprint(slot.Position, p.FactoryFootprintRadius));
                foreach (var start in c.Starts) Assert.True(map.SupportsSweep(start.ExplorerAnchor, site.Position, p.ExplorerCollisionRadius));
            }
            for (int i = 0; i < 8; i++)
            {
                Assert.AreEqual(i, c.Assignments[i]); Assert.True(map.TryLocate(c.Starts[i].ExplorerAnchor, p.ExplorerCollisionRadius, out var origin));
                for (int j = 0; j < 8; j++)
                {
                    Assert.AreEqual(c.RouteCost(i, j), c.RouteCost(j, i));
                    Assert.AreEqual(i == j, c.RouteCost(i, j) == 0);
                    Assert.True(map.TryTraverse(origin, c.Starts[j].ExplorerAnchor, p.ExplorerCollisionRadius, out var target));
                    Assert.True(map.CompatibleCombatSurface(origin, target));
                }
            }
            Assert.False(map.SupportsFootprint(new NavPoint(map.HalfExtent, 0), p.ExplorerCollisionRadius));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.Headquarters((PlayableOwner)8));
            var other = map.Configuration(p, 7108, roster);
            var bytes = new OfflineParticipantAuthority(c, 4).CaptureBytes();
            CollectionAssert.AreEqual(bytes, new OfflineParticipantAuthority(other, 4).CaptureBytes());
            CollectionAssert.AreEqual(bytes, OfflineParticipantAuthority.Restore(bytes, c).CaptureBytes());
            var costs = Enumerable.Range(0, 8).Select(i => Enumerable.Range(0, 8).Select(j => c.RouteCost(i, j)).ToArray()).ToArray();
            string descriptor = PlayableAiCanonical.Encode(new { map.Id, map.Revision, map.HalfExtent, map.DirectFireHeight, map.Supports, c.Starts, c.Sites, c.Obstacles, RouteCosts = costs });
            var root = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
            while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "workflow/project.json"))) root = root.Parent;
            Assert.NotNull(root, "Code project root for descriptor evidence");
            string folder = System.IO.Path.Combine(root.FullName, ".local/ai-test-map");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "descriptor.canonical.txt"), descriptor);
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "descriptor.sha256"), PlayableAiCanonical.Hash(descriptor));
            TestContext.WriteLine("map=" + map.Id + "@" + map.Revision + " capacity=8 content_sha256=" + PlayableAiCanonical.Hash(descriptor));
        }
        [Test] public void EightAiLobbyOwnersBindToIndependentStarts()
        {
            var p = Profile(); var setup = new NativeLobbyConfiguration { AiTestMap = true, HasExplicitSeed = true, ExplicitSeed = 7108 };
            setup.InitializeParticipants(); Assert.True(setup.Spectator); Assert.AreEqual(8, setup.Capacity(p));
            Assert.IsNull(setup.Validate(p, false, false)); Assert.IsNotNull(setup.AddParticipant(false, 0, 8));
            using (var runtime = PlayableRuntime.CreateLobbyMatch(p, 4, setup, true))
            {
                Assert.AreEqual(8, runtime.Latest.Participants.Count); Assert.True(runtime.Latest.Participants.All(o => o.Control == OfflineControl.Ai));
                var frame = runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                Assert.AreEqual(8, frame.Overview.Buildings.Count(b => b.Kind == PlayableBuildingKind.Headquarters));
                Assert.AreEqual(8, frame.Overview.Entities.Where(e => e.Kind == PlayableEntityKind.Explorer).Select(e => e.Position).Distinct().Count());
                Assert.IsNull(runtime.Latest.Failure); Assert.AreEqual(0, runtime.Latest.Metrics.Errors);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var scheduler = typeof(PlayableRuntime).GetField("aiScheduler", flags).GetValue(runtime);
                var owners = ((System.Collections.IEnumerable)scheduler.GetType().GetProperty("Owners", flags).GetValue(scheduler)).Cast<object>().ToArray();
                Assert.AreEqual(8, owners.Length);
                runtime.RequestStop(); Assert.True(SpinWait.SpinUntil(() => runtime.IsStopped, 3000));
            }
        }
    }
}

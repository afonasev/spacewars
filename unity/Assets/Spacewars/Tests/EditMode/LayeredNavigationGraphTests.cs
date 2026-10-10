using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class LayeredNavigationGraphTests
    {
        private static readonly NavClearanceProfile Small = new NavClearanceProfile("small", 1, "ground", 1, .5);
        private static readonly NavClearanceProfile Large = new NavClearanceProfile("large", 1, "ground", 1, 1);
        // Exact immutable fixture profile data binding, not a process hash.
        private static string Sha(string data)
        { using(var hash=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data))).Replace("-", "").ToLowerInvariant(); }
        private static readonly string ProfileBinding = Sha("stacked-profile-v1;distance-cost=authored;grid-cell=1");
        private static readonly NavGraphProfile Profile = new NavGraphProfile(ProfileBinding, "fixture-distance-v1", 1, 1);
        private static readonly NavGraphCompileLimits Limits = new NavGraphCompileLimits(2000, 2000, 10000);
        private static NavLocation At(double x, double z, string layer) => new NavLocation(new NavPoint(x, z), layer);
        private static LayeredNavigationGraph Graph(StackedNavigationFixture terrain, NavClearanceProfile clearance) => LayeredNavigationGraphCompiler.Compile(terrain.Provider, clearance, Profile, Limits);

        [Test] public void BudgetedCompileRestoresFromEveryPhaseWithoutChangingTypedGraph()
        {
            var provider = new StackedNavigationFixture().Provider;
            var baseline = new LayeredNavigationCompileWork(provider, Small, Profile, Limits);
            while (baseline.Phase != LayeredNavigationCompileWork.Stage.Ready) Assert.LessOrEqual(baseline.Advance(17), 17);
            var resumed = new LayeredNavigationCompileWork(provider, Small, Profile, Limits);
            var observed = new HashSet<LayeredNavigationCompileWork.Stage>();
            while (resumed.Phase != LayeredNavigationCompileWork.Stage.Ready) {
                var phase = resumed.Phase;
                if (observed.Add(phase)) resumed = LayeredNavigationCompileWork.Restore(provider, Small, Profile, Limits, resumed.Save());
                Assert.AreEqual(1, resumed.Advance(1));
            }
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(LayeredNavigationCompileWork.Stage)).Cast<LayeredNavigationCompileWork.Stage>().Where(p => p != LayeredNavigationCompileWork.Stage.Ready), observed);
            Assert.AreEqual(baseline.Consumed, resumed.Consumed);
            CollectionAssert.AreEqual(baseline.Graph.Nodes.Select(n => Tuple.Create(n.Location, n.Component)), resumed.Graph.Nodes.Select(n => Tuple.Create(n.Location, n.Component)));
            CollectionAssert.AreEqual(baseline.Graph.Arcs.Select(a => Tuple.Create(a.From, a.To, a.Cost, a.Kind, a.PortalId)),
                resumed.Graph.Arcs.Select(a => Tuple.Create(a.From, a.To, a.Cost, a.Kind, a.PortalId)));
            Assert.AreEqual(1, resumed.Graph.Arcs.Count(a => a.Kind == NavGraphArcKind.Portal && a.From != a.To) / 2);
            Assert.Throws<ArgumentException>(() => LayeredNavigationCompileWork.Restore(provider, Large, Profile, Limits, resumed.Save()));
        }

        [Test] public void ProductionTerrainRetainsBothFloorsAndRequiresExplicitLayer()
        {
            var terrain = new StackedNavigationFixture(); ILayeredPlayableTerrain contract = terrain;
            var lower = At(0, 0, "lower"); var upper = At(0, 0, "upper");
            Assert.True(contract.IsValidLocation(lower, .5)); Assert.True(contract.IsValidLocation(upper, .5));
            Assert.False(contract.TryLocate(lower.Position, .5, out _)); Assert.IsNull(contract.SupportAt(lower.Position));
            Assert.Throws<ArgumentException>(() => contract.SurfaceHeight(lower.Position));
            Assert.False(contract.CompatibleCombatSurface(lower, upper));
            Assert.True(contract.TryTraverse(lower, new NavPoint(3, 0), .5, out var end)); Assert.AreEqual("lower", end.SurfaceId);
            Assert.False(terrain.Provider.CanSweep(lower, upper, Small));
            Assert.False(contract.IsValidLocation(At(0, 0, "missing"), .5));
            // Upper-only blocker must not erase the walkable lower floor.
            Assert.True(terrain.Provider.CanSweep(At(3, 0, "lower"), At(6, 0, "lower"), Small));
            Assert.False(terrain.Provider.CanSweep(At(3, 0, "upper"), At(6, 0, "upper"), Small));
        }
        [Test] public void ExactIngressTraversalAndEgressHavePhysicalWeightedCost()
        {
            var p = new StackedNavigationFixture().Provider;
            var origin = At(-5.125, .25, "lower"); var endpoint = At(3.125, .125, "upper");
            var result = p.Connect("ramp", origin, endpoint, Small);
            Assert.AreEqual(NavConnectorStatus.Ready, result.Status); Assert.AreEqual(origin, result.Origin); Assert.AreEqual(endpoint, result.Endpoint);
            Assert.AreEqual(StackedNavigationFixture.Entry, result.Entry); Assert.AreEqual(StackedNavigationFixture.Exit, result.Exit);
            Assert.AreEqual(Math.Sqrt(3.125 * 3.125 + .25 * .25) + 6 + 2 * Math.Sqrt(1.125 * 1.125 + .125 * .125), result.Cost, 1e-12);
            Assert.AreEqual(NavConnectorStatus.Ready, p.Connect("ramp", endpoint, origin, Small).Status);
            Assert.AreEqual(NavConnectorStatus.InvalidLocation, p.Connect("ramp", origin, At(3, 0, "lower"), Small).Status);
            Assert.AreEqual(NavConnectorStatus.Blocked, p.Connect("ramp", origin, At(6, 0, "upper"), Small).Status); // unsafe egress
        }
        [Test] public void NarrowPortalBlocksLargeActorWithoutShrinkingItsRadius()
        {
            var t = new StackedNavigationFixture();
            Assert.True(t.Provider.IsValid(StackedNavigationFixture.Entry, Large)); Assert.True(t.Provider.IsValid(StackedNavigationFixture.Exit, Large));
            Assert.AreEqual(NavConnectorStatus.Blocked, t.Provider.Connect("ramp", StackedNavigationFixture.Entry, StackedNavigationFixture.Exit, Large).Status);
            var small = Graph(t, Small); var large = Graph(t, Large);
            Assert.AreEqual(NavConnectorStatus.Blocked, large.PortalQualifications.Single().Status); Assert.AreEqual(1, Large.Radius);
            Assert.AreEqual(2, small.Arcs.Count(a => a.Kind == NavGraphArcKind.Portal)); Assert.False(large.Arcs.Any(a => a.Kind == NavGraphArcKind.Portal));
            Assert.AreEqual(1, small.Nodes.Select(n => n.Component).Distinct().Count()); Assert.AreEqual(2, large.Nodes.Select(n => n.Component).Distinct().Count());
            Assert.AreNotEqual(small.Binding, large.Binding);
            Assert.Throws<ArgumentException>(() => large.PortalKey(0, "ramp"));
            var wide = new NavClearanceProfile("wide",1,"ground",1,2);
            Assert.False(t.Provider.IsValid(StackedNavigationFixture.Exit,wide));
            Assert.AreEqual(NavConnectorStatus.Blocked,t.Provider.QualifyPortal("ramp",wide).Status);
            Assert.AreEqual(NavConnectorStatus.Blocked,Graph(t,wide).PortalQualifications.Single().Status);
            Assert.AreEqual(NavConnectorStatus.InvalidLocation,t.Provider.Connect("ramp",StackedNavigationFixture.Entry,StackedNavigationFixture.Exit,wide).Status);
        }
        [Test] public void DirectedAndClosedPortalsRemainDistinctFromPhysicalBlockage()
        {
            var t = new StackedNavigationFixture(direction: NavPortalDirection.Forward);
            Assert.AreEqual(NavConnectorStatus.WrongDirection, t.Provider.Connect("ramp", StackedNavigationFixture.Exit, StackedNavigationFixture.Entry, Small).Status);
            var graph = Graph(t, Small); var arc = graph.Arcs.Single(a => a.Kind == NavGraphArcKind.Portal);
            Assert.AreEqual("lower", graph.Nodes[arc.From].Location.SurfaceId); Assert.AreEqual("upper", graph.Nodes[arc.To].Location.SurfaceId); Assert.AreEqual(6, arc.Cost);
            var closed = new StackedNavigationFixture(open: false); var g = Graph(closed, Small);
            Assert.AreEqual(NavConnectorStatus.Closed, g.PortalQualifications.Single().Status); Assert.False(g.Arcs.Any(a => a.Kind == NavGraphArcKind.Portal));
            Assert.AreEqual(2, g.Nodes.Select(n => n.Component).Distinct().Count()); Assert.AreNotEqual(graph.Binding, g.Binding);
            Assert.AreEqual(NavConnectorStatus.UnknownPortal, t.Provider.Connect("missing", StackedNavigationFixture.Entry, StackedNavigationFixture.Exit, Small).Status);
            var air = new NavClearanceProfile("air", 1, "air", 1, .5);
            Assert.AreEqual(NavConnectorStatus.Blocked, t.Provider.Connect("ramp", StackedNavigationFixture.Entry, StackedNavigationFixture.Exit, air).Status);
        }
        [Test] public void EveryGraphArcIsQualifiedAgainstProductionGeometryAndExactAttachments()
        {
            var t = new StackedNavigationFixture(); var graph = Graph(t, Small);
            foreach (var node in graph.Nodes) Assert.True(t.Provider.IsValid(node.Location, Small));
            foreach (var arc in graph.Arcs) {
                var from = graph.Nodes[arc.From].Location; var to = graph.Nodes[arc.To].Location;
                if (arc.Kind == NavGraphArcKind.Surface) { Assert.AreEqual(from.SurfaceId, to.SurfaceId); Assert.True(t.Provider.CanSweep(from, to, Small)); }
                else Assert.AreEqual(NavConnectorStatus.Ready, t.Provider.Connect(arc.PortalId, from, to, Small).Status);
            }
            var exact = At(.125, 2.25, "upper"); var attached = graph.Attach(exact);
            Assert.IsNotEmpty(attached); Assert.True(attached.All(a => a.ExactLocation.Equals(exact) && graph.Nodes[a.NodeId].Location.SurfaceId == "upper"));
            Assert.Throws<ArgumentException>(() => graph.Attach(At(4.5, 0, "upper")));
            Assert.True(graph.Nodes.Any(n => n.Location.Equals(At(0, 0, "lower")))); Assert.True(graph.Nodes.Any(n => n.Location.Equals(At(0, 0, "upper"))));
        }
        [Test] public void ConcaveWalkableBoundaryCannotBeCrossedByFootprintOrSweep()
        {
            var area = new NavTraversalArea(new NavPolygon(new[] { new NavPoint(-3,-3), new NavPoint(3,-3), new NavPoint(3,3), new NavPoint(1,3), new NavPoint(1,-1), new NavPoint(-1,-1), new NavPoint(-1,3), new NavPoint(-3,3) }), 8, Array.Empty<NavObstacle>());
            Assert.True(area.Contains(new NavPoint(-2, 2), .25)); Assert.True(area.Contains(new NavPoint(2, 2), .25));
            Assert.False(area.Sweep(new NavPoint(-2, 2), new NavPoint(2, 2), .25)); Assert.False(area.Sweep(new NavPoint(-2, 2), new NavPoint(2, 2), 0));
            Assert.False(area.Contains(new NavPoint(-1.1, 2), .25));
        }
        [Test] public void StableBindingsCoverGeometrySemanticsCostsMobilityAndProfile()
        {
            var p = new StackedNavigationFixture().Provider;
            var reordered = new LayeredNavigationProvider(p.Id, p.Revision, p.SemanticsVersion, p.TopologyRevision, p.ExactMapBinding, p.Surfaces.Reverse(), p.Portals.Reverse());
            Assert.AreEqual(p.Binding, reordered.Binding);
            Assert.AreEqual(Graph(new StackedNavigationFixture(), Small).Binding, Graph(new StackedNavigationFixture(), Small).Binding);
            foreach (var variant in new[] {
                new LayeredNavigationProvider(p.Id, 2, p.SemanticsVersion, 1, p.ExactMapBinding, p.Surfaces, p.Portals),
                new LayeredNavigationProvider(p.Id, 1, 3, 1, p.ExactMapBinding, p.Surfaces, p.Portals),
                new LayeredNavigationProvider(p.Id, 1, 2, 2, p.ExactMapBinding, p.Surfaces, p.Portals),
                new LayeredNavigationProvider(p.Id, 1, 2, 1, new string('b',64), p.Surfaces, p.Portals),
                new LayeredNavigationProvider(p.Id, 1, 2, 1, p.ExactMapBinding, new[] { new NavLayerSurface("lower","lower-combat",0,3,p.Surfaces.Single(s=>s.Id=="lower").Area), p.Surfaces.Single(s=>s.Id=="upper") }, p.Portals),
                new LayeredNavigationProvider(p.Id, 1, 2, 1, p.ExactMapBinding, new[] { new NavLayerSurface("lower","lower-combat",0,1,StackedNavigationFixture.Area(-7,-8,8,8)), p.Surfaces.Single(s=>s.Id=="upper") }, p.Portals) }) Assert.AreNotEqual(p.Binding, variant.Binding);
            var graph = Graph(new StackedNavigationFixture(), Small);
            foreach (var profile in new[] { new NavGraphProfile(new string('b',64),"fixture-distance-v1",1,1), new NavGraphProfile(ProfileBinding,"fixture-distance-v1",2,1), new NavGraphProfile(ProfileBinding,"fixture-distance-v1",1,2) }) Assert.AreNotEqual(graph.Binding, LayeredNavigationGraphCompiler.Compile(p,Small,profile,Limits).Binding);
            Assert.AreNotEqual(graph.Binding, LayeredNavigationGraphCompiler.Compile(p,new NavClearanceProfile("small",1,"ground",2,.5),Profile,Limits).Binding);
        }
        [Test] public void SnapshotOrderAndIdentitySurviveDeclarationReorderingAndLargerCaps()
        {
            var t = new StackedNavigationFixture(); var p = t.Provider; var graph = Graph(t, Small);
            var layers = p.Surfaces.Reverse().ToArray(); var portals = p.Portals.ToArray();
            var reordered = new LayeredNavigationProvider(p.Id,p.Revision,p.SemanticsVersion,p.TopologyRevision,p.ExactMapBinding,layers,portals);
            layers[0] = null; portals[0] = null; // caller arrays are never retained
            var other = LayeredNavigationGraphCompiler.Compile(reordered,Small,Profile,new NavGraphCompileLimits(4000,4000,20000));
            Assert.AreEqual(graph.Binding,other.Binding);
            CollectionAssert.AreEqual(graph.Nodes.Select(n=>Tuple.Create(n.Id,n.Location,n.Component)),other.Nodes.Select(n=>Tuple.Create(n.Id,n.Location,n.Component)));
            CollectionAssert.AreEqual(graph.Arcs.Select(a=>Tuple.Create(a.From,a.To,a.Cost,a.Kind,a.PortalId)),other.Arcs.Select(a=>Tuple.Create(a.From,a.To,a.Cost,a.Kind,a.PortalId)));
            Assert.AreEqual(NavPortalDirection.Both,graph.Portals.Single().Direction);
            Assert.AreEqual(.75,graph.Portals.Single().Corridor.Boundary.MaxZ);
            Assert.Throws<NotSupportedException>(()=>((IList<NavGraphNode>)graph.Nodes).Clear());
            Assert.Throws<NotSupportedException>(()=>((IList<NavLayerSurface>)graph.Surfaces).Clear());
        }
        [Test] public void SharedArtifactIdentityDoesNotMergeAuthoritySubscriptions()
        {
            var graph = Graph(new StackedNavigationFixture(), Small); var requested = At(1.125, 2.25, "upper"); var candidates = new[] { requested, At(2.125, 2.25, "upper") };
            var region = new NavTerminalRegion("terminal",1,requested,candidates); candidates[0] = At(0,0,"lower"); Assert.AreEqual(requested, region.Candidates[0]);
            var key = graph.RegionKey(0,region); Assert.AreEqual(key,graph.RegionKey(0,region));
            Assert.AreNotEqual(key,graph.RegionKey(0,new NavTerminalRegion("terminal",2,requested,new[]{requested})));
            NavRouteSubscription Subscription(long group=1,long activation=1,long incarnation=1,long request=1,string hold="none",NavLocation? endpoint=null) => new NavRouteSubscription(1,group,1,1,0,activation,1,incarnation,1,1,request,At(-4,0,"lower"),endpoint??requested,hold);
            var root = Subscription();
            foreach (var other in new[] { Subscription(group:2),Subscription(activation:2),Subscription(incarnation:2),Subscription(request:2),Subscription(hold:"held-footprints-sha"),Subscription(endpoint:At(1.125,2.25,"lower")) }) Assert.AreNotEqual(root.StableKey,other.StableKey);
            Assert.AreEqual(root.StableKey,Subscription().StableKey); // unaffected member is independent of global membership
            Assert.Throws<ArgumentException>(()=>new NavTerminalRegion("bad",1,requested,new[]{At(1,2,"lower")}));
            Assert.Throws<ArgumentException>(()=>graph.RegionKey(0,new NavTerminalRegion("blocked",1,At(4.5,0,"upper"),new[]{At(4.5,0,"upper")})));
            Assert.IsNotNull(graph.PortalKey(0,"ramp"));
        }
        [Test] public void MalformedPortalAndMetadataFailBeforeSnapshotPublication()
        {
            var p = new StackedNavigationFixture().Provider; var portal = p.Portals.Single();
            LayeredNavigationProvider Build(params NavLayerPortal[] links) => new LayeredNavigationProvider(p.Id,1,2,1,p.ExactMapBinding,p.Surfaces,links);
            Assert.Throws<ArgumentException>(()=>Build(portal,portal));
            Assert.Throws<ArgumentException>(()=>new LayeredNavigationProvider(p.Id,1,2,1,p.ExactMapBinding,new[]{p.Surfaces[0],p.Surfaces[0]},new[]{portal}));
            foreach(var entry in new[]{At(-2,0,"missing"),At(9,0,"lower")}) Assert.Throws<ArgumentException>(()=>Build(new NavLayerPortal("bad",entry,portal.Exit,NavPortalDirection.Both,true,6,portal.Corridor,new[]{"ground"})));
            Assert.Throws<ArgumentException>(()=>Build(new NavLayerPortal("bad",portal.Entry,portal.Exit,NavPortalDirection.Both,true,6,StackedNavigationFixture.Area(-3,1,3,2),new[]{"ground"})));
            Assert.Throws<ArgumentException>(()=>new NavLayerPortal("bad",portal.Entry,portal.Exit,(NavPortalDirection)99,true,6,portal.Corridor,new[]{"ground"}));
            Assert.Throws<ArgumentException>(()=>new NavLayerPortal("bad",portal.Entry,portal.Exit,NavPortalDirection.Both,true,6,portal.Corridor,new[]{"ground","ground"}));
            Assert.Throws<ArgumentException>(()=>new NavLayerSurface("bad","family",double.NaN,1,p.Surfaces[0].Area));
            foreach(var cost in new[]{0d,-1d,double.NaN,double.PositiveInfinity}) Assert.Throws<ArgumentException>(()=>new NavLayerPortal("bad",portal.Entry,portal.Exit,NavPortalDirection.Both,true,cost,portal.Corridor,new[]{"ground"}));
            Assert.Throws<ArgumentException>(()=>new NavClearanceProfile("bad",1,"ground",1,-.1));
            Assert.Throws<ArgumentException>(()=>new NavGraphProfile("not-an-exact-binding","cost",1,1));
            Assert.Throws<ArgumentException>(()=>LayeredNavigationGraphCompiler.Compile(p,Small,Profile,new NavGraphCompileLimits(10,2000,10000)));
            Assert.Throws<ArgumentException>(()=>LayeredNavigationGraphCompiler.Compile(p,Small,Profile,new NavGraphCompileLimits(2000,10,10000)));
            Assert.Throws<ArgumentException>(()=>LayeredNavigationGraphCompiler.Compile(p,Small,Profile,new NavGraphCompileLimits(2000,2000,10)));
        }
    }
}

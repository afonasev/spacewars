using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class LayeredNavigationSolverTests
    {
        private static NavLocation At(double x, double z, string surface) => new NavLocation(new NavPoint(x,z), surface);
        private static readonly NavClearanceProfile Small = new NavClearanceProfile("small",1,"ground",1,.5);
        private static readonly NavGraphProfile Profile = new NavGraphProfile(new string('a',64),"weighted",1,1);
        private static LayeredNavigationGraph Graph(StackedNavigationFixture fixture, NavClearanceProfile clearance = null)
            => LayeredNavigationGraphCompiler.Compile(fixture.Provider,clearance ?? Small,Profile,new NavGraphCompileLimits(2000,2000,10000));
        private static NavRouteSubscription Member(NavLocation origin, NavLocation endpoint, long group=1, long entity=1, long request=1)
            => new NavRouteSubscription(1,group,1,group,0,1,entity,1,1,1,request,origin,endpoint,"none");
        private static void Complete(NavIncrementalSingletonRoute route, int quota=1)
        { for(int i=0; i<100000 && route.Status==NavSolveStatus.Pending; i++) Assert.LessOrEqual(route.Advance(quota).Consumed,quota); Assert.AreNotEqual(NavSolveStatus.Pending,route.Status); }
        private static void Complete(NavSharedTerminalField field, int quota=1)
        { for(int i=0; i<200000 && field.Status==NavSolveStatus.Pending; i++) Assert.LessOrEqual(field.Advance(quota).Consumed,quota); Assert.AreNotEqual(NavSolveStatus.Pending,field.Status); }
        private static void Complete(NavIncrementalSharedRoute route, int quota=1)
        { for(int i=0; i<100000 && route.Status==NavSolveStatus.Pending; i++) Assert.LessOrEqual(route.Advance(quota).Consumed,quota); Assert.AreNotEqual(NavSolveStatus.Pending,route.Status); }
        private static void Safe(LayeredNavigationGraph graph, NavTypedRoute route, LayeredNavigationProvider provider = null)
        {
            Assert.IsNotNull(route); Assert.AreEqual(route.Subscription.Origin,route.Legs.First().From);
            Assert.AreEqual(route.Subscription.AssignedEndpoint,route.Legs.Last().To);
            for(int i=0;i<route.Legs.Count;i++) {
                var leg=route.Legs[i]; if(i>0) Assert.AreEqual(route.Legs[i-1].To,leg.From);
                if(leg.Kind==NavTypedLegKind.Surface) Assert.True(graph.CanSweep(leg.From,leg.To));
                else Assert.AreEqual(NavConnectorStatus.Ready,(provider ?? new StackedNavigationFixture().Provider).Connect(leg.PortalId,leg.From,leg.To,graph.Binding.Clearance).Status);
            }
            Assert.AreEqual(route.Legs.Sum(l=>l.Cost),route.Cost,1e-10);
        }
        [Test] public void EveryPrimitiveStepCanResumeWithoutChangingTypedRoute()
        {
            var graph = Graph(new StackedNavigationFixture());
            var origin = At(-5.125, .25, "lower"); var end = At(2.625, 2.625, "upper");
            var subscription = Member(origin, end);
            var singleton = new NavIncrementalSingletonRoute(graph, subscription);
            for (int i = 0; i < 100000 && singleton.Status == NavSolveStatus.Pending; i++) {
                singleton.Advance(1);
                singleton = NavIncrementalSingletonRoute.Restore(graph, subscription, singleton.Save());
            }
            Assert.AreEqual(NavSolveStatus.Ready, singleton.Status); Safe(graph, singleton.Route);
            var region = new NavTerminalRegion("checkpoint-terminal", 1, At(2.25, 2.25, "upper"),
                new[] { At(2.25, 2.25, "upper"), At(1.25, 2.25, "upper") });
            var field = new NavSharedTerminalField(graph, region, graph.Nodes.Count * 2);
            for (int i = 0; i < 100000 && field.Status == NavSolveStatus.Pending; i++) {
                field.Advance(1);
                field = NavSharedTerminalField.Restore(graph, region, graph.Nodes.Count * 2, field.Save());
            }
            Assert.AreEqual(NavSolveStatus.Ready, field.Status);
            var connector = new NavIncrementalSharedRoute(field, subscription);
            for (int i = 0; i < 100000 && connector.Status == NavSolveStatus.Pending; i++) {
                connector.Advance(1);
                connector = NavIncrementalSharedRoute.Restore(field, subscription, connector.Save());
            }
            Assert.AreEqual(NavSolveStatus.Ready, connector.Status); Safe(graph, connector.Route);
            Assert.True(connector.Route.Legs.Any(leg => leg.Kind == NavTypedLegKind.Portal));
            Assert.Throws<ArgumentException>(() => NavIncrementalSharedRoute.Restore(field,
                Member(origin, end, group: 2), connector.Save()));
        }
        [Test] public void SingletonDirectUsesOneExactSweep()
        {
            var graph=Graph(new StackedNavigationFixture()); var origin=At(-6.125,2.25,"lower"); var end=At(-3.125,2.25,"lower");
            var route=new NavIncrementalSingletonRoute(graph,Member(origin,end));
            Assert.AreEqual(NavSolveStatus.Ready,route.Advance(1).Status); Assert.AreEqual(1,route.Route.Legs.Count); Safe(graph,route.Route);
        }
        [Test] public void SingletonWeightedDetourUsesTypedPortalAndExactEnds()
        {
            var graph=Graph(new StackedNavigationFixture()); var origin=At(-5.125,.25,"lower"); var end=At(3.125,.125,"upper");
            var route=new NavIncrementalSingletonRoute(graph,Member(origin,end));
            Assert.AreEqual(NavSolveStatus.Pending,route.Advance(1).Status); Complete(route,17);
            Assert.AreEqual(NavSolveStatus.Ready,route.Status); Safe(graph,route.Route);
            Assert.AreEqual(1,route.Route.Legs.Count(l=>l.Kind==NavTypedLegKind.Portal));
            Assert.AreEqual("ramp",route.Route.Legs.Single(l=>l.Kind==NavTypedLegKind.Portal).PortalId);
            Assert.AreEqual(new StackedNavigationFixture().Provider.Connect("ramp",origin,end,Small).Cost,route.Route.Cost,1e-10);
        }
        [Test] public void SameXZOnDifferentFloorsStillRequiresDeclaredPortal()
        {
            var graph=Graph(new StackedNavigationFixture()); var origin=At(0,0,"lower"); var end=At(0,0,"upper");
            var route=new NavIncrementalSingletonRoute(graph,Member(origin,end)); Complete(route,11);
            Assert.AreEqual(NavSolveStatus.Ready,route.Status); Safe(graph,route.Route);
            Assert.AreEqual(1,route.Route.Legs.Count(l=>l.Kind==NavTypedLegKind.Portal));
            Assert.Greater(route.Route.Cost,0);
        }
        [Test] public void WeightedSearchChoosesLongerCheapPortalOverShortExpensivePortal()
        {
            var area=StackedNavigationFixture.Area(-8,-8,8,8);
            var lower=new NavLayerSurface("lower","lower",0,1,area);
            var upper=new NavLayerSurface("upper","upper",4,1,area);
            var corridor=StackedNavigationFixture.Area(-4,-1,1,1);
            var expensive=new NavLayerPortal("expensive",At(0,0,"lower"),At(0,0,"upper"),NavPortalDirection.Both,true,30,corridor,new[]{"ground"});
            var cheap=new NavLayerPortal("cheap",At(-3,0,"lower"),At(-3,0,"upper"),NavPortalDirection.Both,true,1,corridor,new[]{"ground"});
            var provider=new LayeredNavigationProvider("weighted-alternatives",1,1,1,new string('c',64),new[]{lower,upper},new[]{expensive,cheap});
            var graph=LayeredNavigationGraphCompiler.Compile(provider,Small,Profile,new NavGraphCompileLimits(2000,2000,10000));
            var route=new NavIncrementalSingletonRoute(graph,Member(At(0,0,"lower"),At(0,0,"upper"))); Complete(route,13);
            Assert.AreEqual(NavSolveStatus.Ready,route.Status); Safe(graph,route.Route,provider);
            Assert.AreEqual(7,route.Route.Cost,1e-10);
            Assert.AreEqual("cheap",route.Route.Legs.Single(l=>l.Kind==NavTypedLegKind.Portal).PortalId);
            Assert.Greater(graph.Arcs.First(a=>a.Kind==NavGraphArcKind.Portal && a.PortalId=="expensive").Cost,route.Route.Cost);
        }
        [Test] public void SharedFieldReusedAcrossDistinctStartsSlotsAndAuthorityGroups()
        {
            var graph=Graph(new StackedNavigationFixture()); var center=At(2.25,2.25,"upper");
            var region=new NavTerminalRegion("upper-terminal",1,center,new[]{center,At(1.25,2.25,"upper")});
            var field=new NavSharedTerminalField(graph,region,graph.Nodes.Count*2); Complete(field,23);
            Assert.AreEqual(NavSolveStatus.Ready,field.Status);
            int labelsBefore=field.LabelCount;
            int representative=graph.Nodes.First(n=>field.TryGet(n.Id,0,out _)).Id;
            Assert.True(field.TryGet(representative,0,out double costBefore));
            var first=new NavIncrementalSharedRoute(field,Member(At(-5.125,.25,"lower"),At(2.625,2.625,"upper"),1,1));
            var second=new NavIncrementalSharedRoute(field,Member(At(-4.625,-2.25,"lower"),At(1.375,2.75,"upper"),2,2));
            Complete(first,1); Complete(second,29);
            Assert.AreEqual(NavSolveStatus.Ready,first.Status); Assert.AreEqual(NavSolveStatus.Ready,second.Status);
            Assert.AreNotEqual(first.Route.Subscription.StableKey,second.Route.Subscription.StableKey);
            Assert.AreEqual(labelsBefore,field.LabelCount); Assert.AreEqual(0,field.Advance(100).Consumed);
            Assert.True(field.TryGet(representative,0,out double costAfter)); Assert.AreEqual(costBefore,costAfter);
            Safe(graph,first.Route); Safe(graph,second.Route);
            Assert.True(first.Route.Legs.Any(l=>l.Kind==NavTypedLegKind.Portal)); Assert.True(second.Route.Legs.Any(l=>l.Kind==NavTypedLegKind.Portal));
        }
        [Test] public void DirectionClosureAndRadiusCannotBecomeFalseReachability()
        {
            var lower=At(-4,0,"lower"); var upper=At(3,0,"upper");
            foreach(var fixture in new[]{new StackedNavigationFixture(false),new StackedNavigationFixture(true,NavPortalDirection.Forward)}) {
                var graph=Graph(fixture); var route=new NavIncrementalSingletonRoute(graph,Member(upper,lower)); Complete(route,31);
                Assert.AreEqual(NavSolveStatus.UnreachableInGraph,route.Status);
            }
            var large=new NavClearanceProfile("large",1,"ground",1,1); var largeGraph=Graph(new StackedNavigationFixture(),large);
            var blocked=new NavIncrementalSingletonRoute(largeGraph,Member(lower,At(2,2,"upper"))); Complete(blocked,31);
            Assert.AreEqual(NavSolveStatus.UnreachableInGraph,blocked.Status);
        }
        [Test] public void InvalidEndpointAndFiniteCapacityAreDistinctFromPending()
        {
            var graph=Graph(new StackedNavigationFixture());
            Assert.AreEqual(NavSolveStatus.InvalidEndpoint,new NavIncrementalSingletonRoute(graph,Member(At(0,0,"missing"),At(0,0,"lower"))).Status);
            var center=At(2,2,"upper"); var region=new NavTerminalRegion("terminal",1,center,new[]{center});
            var field=new NavSharedTerminalField(graph,region,1); Complete(field);
            Assert.AreEqual(NavSolveStatus.CapacityExceeded,field.Status);
            var singletonCap=new NavIncrementalSingletonRoute(graph,Member(At(-4,0,"lower"),center),1); Complete(singletonCap);
            Assert.AreEqual(NavSolveStatus.CapacityExceeded,singletonCap.Status);
            var pending=new NavIncrementalSharedRoute(new NavSharedTerminalField(graph,region,graph.Nodes.Count),Member(At(-4,0,"lower"),center));
            Assert.AreEqual(0,pending.Advance(4).Consumed); Assert.AreEqual(NavSolveStatus.Pending,pending.Status);
        }
        [Test] public void ValidExactEndpointsWithoutSampledConnectorAreNotCalledUnreachable()
        {
            var area=StackedNavigationFixture.Area(.1,.1,.9,.9);
            var lower=new NavLayerSurface("lower","lower",0,1,area);
            var upper=new NavLayerSurface("upper","upper",4,1,area);
            var provider=new LayeredNavigationProvider("tiny",1,1,1,new string('b',64),new[]{lower,upper},Array.Empty<NavLayerPortal>());
            var clearance=new NavClearanceProfile("tiny",1,"ground",1,.05);
            var graph=LayeredNavigationGraphCompiler.Compile(provider,clearance,Profile,new NavGraphCompileLimits(10,10,10));
            Assert.AreEqual(0,graph.Nodes.Count);
            var route=new NavIncrementalSingletonRoute(graph,Member(At(.3,.3,"lower"),At(.7,.7,"upper"))); Complete(route);
            Assert.AreEqual(NavSolveStatus.BlockedConnector,route.Status);
        }
        [Test] public void SharedFieldAndMemberContinuationMatchAcrossQuotaChunking()
        {
            var graph=Graph(new StackedNavigationFixture()); var center=At(2,2,"upper");
            var region=new NavTerminalRegion("region",1,center,new[]{center,At(3,2,"upper")});
            var a=new NavSharedTerminalField(graph,region,graph.Nodes.Count*2);
            var b=new NavSharedTerminalField(graph,region,graph.Nodes.Count*2);
            Complete(a,1); Complete(b,127);
            Assert.AreEqual(NavSolveStatus.Ready,a.Status); Assert.AreEqual(a.LabelCount,b.LabelCount); Assert.AreEqual(a.StableKey,b.StableKey);
            var member=Member(At(-4.25,.25,"lower"),At(2.25,2.25,"upper"));
            var first=new NavIncrementalSharedRoute(a,member); var second=new NavIncrementalSharedRoute(b,member);
            Complete(first,1); Complete(second,101);
            Assert.AreEqual(NavSolveStatus.Ready,first.Status); Assert.AreEqual(first.Route.Cost,second.Route.Cost,1e-10);
            CollectionAssert.AreEqual(first.Route.Legs.Select(l=>Tuple.Create(l.From,l.To,l.Kind,l.PortalId)),second.Route.Legs.Select(l=>Tuple.Create(l.From,l.To,l.Kind,l.PortalId)));
        }
        [Test] public void ChunkingAndDeclarationOrderProduceSameTypedPath()
        {
            var fixture=new StackedNavigationFixture(); var graph=Graph(fixture); var origin=At(-5,.5,"lower"); var end=At(2.5,2.5,"upper");
            var first=new NavIncrementalSingletonRoute(graph,Member(origin,end)); Complete(first,1);
            var otherProvider=new LayeredNavigationProvider(fixture.Provider.Id,fixture.Provider.Revision,fixture.Provider.SemanticsVersion,fixture.Provider.TopologyRevision,fixture.Provider.ExactMapBinding,fixture.Provider.Surfaces.Reverse(),fixture.Provider.Portals.Reverse());
            var otherGraph=LayeredNavigationGraphCompiler.Compile(otherProvider,Small,Profile,new NavGraphCompileLimits(2000,2000,10000));
            var second=new NavIncrementalSingletonRoute(otherGraph,Member(origin,end)); Complete(second,91);
            Assert.AreEqual(NavSolveStatus.Ready,first.Status); Assert.AreEqual(first.Route.Cost,second.Route.Cost,1e-10);
            CollectionAssert.AreEqual(first.Route.Legs.Select(l=>Tuple.Create(l.From,l.To,l.Kind,l.PortalId)),second.Route.Legs.Select(l=>Tuple.Create(l.From,l.To,l.Kind,l.PortalId)));
        }
    }
}

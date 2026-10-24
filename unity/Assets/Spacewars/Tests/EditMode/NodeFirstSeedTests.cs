using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed class NodeFirstSeedTests
    {
        internal static readonly NavClearanceProfile Clearance=new NavClearanceProfile("node-seed",1,"ground",1,.25);
        internal static readonly NavGraphProfile Profile=new NavGraphProfile(new string('f',64),"distance",1,16);
        internal static readonly NavGraphCompileLimits Limits=new NavGraphCompileLimits(2000,2000,10000);
        internal static PartitionNavigationProvider Provider()=>new PartitionNavigationProvider(ThreeCrossingsMap.Default,new NavGeometry(ThreeCrossingsMap.Default.HalfExtent,ThreeCrossingsMap.Default.MovementBlockers.ToArray(),1),null);
        internal static NavLocation At(double x,double z,string surface="bank/east")=>new NavLocation(new NavPoint(x,z),surface);
        internal static NavTerminalRegion Region(bool reverse=false){var at=At(32,0);var c=new[]{At(32,-16),at,At(32,16)};return new NavTerminalRegion("node-first",1,at,reverse?c.Reverse():c);}
        internal static string Phase(NavSharedTerminalField f)=>typeof(NavSharedTerminalField).GetField("phase",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f).ToString();
        [TestCase(false)][TestCase(true)] public void ActualPartitionOldAndNodeSeedKeepExactLabelsRoutesTiesAndPaidWork(bool reverse)
        {
            var graph=LayeredNavigationGraphCompiler.Compile(Provider(),Clearance,Profile,Limits);var region=Region(reverse);int cap=graph.Nodes.Count*region.Candidates.Count;
            var fast=new NavSharedTerminalField(graph,region,cap);var init=fast.Save();init.Phase=0;var old=NavSharedTerminalField.Restore(graph,region,cap,init);
            int fastSteps=0,oldSteps=0,fastProbes=0,oldProbes=0;graph.BeforeAttachmentProbe=()=>fastProbes++;
            while(!fast.SeedComplete){Assert.Less(++fastSteps,20000);fast.Advance(1);}
            graph.BeforeAttachmentProbe=()=>oldProbes++;while(!old.SeedComplete){Assert.Less(++oldSteps,20000);old.Advance(1);}graph.BeforeAttachmentProbe=null;
            int n=graph.Nodes.Count,c=region.Candidates.Count,w=graph.Nodes.Count(v=>v.Location.SurfaceId!=region.Requested.SurfaceId);
            Assert.Greater(w,0);Assert.AreEqual(n*c+c+1,oldSteps);Assert.AreEqual(c+w+(n-w)*c,fastSteps);Assert.AreEqual((c-1)*w+1,oldSteps-fastSteps);Assert.AreEqual(n*c,oldProbes);Assert.AreEqual((n-w)*c,fastProbes);
            CollectionAssert.AreEqual(old.Save().Frontier,fast.Save().Frontier,"same canonical seed queue; raw layout may differ");
            for(int node=0;node<n;node++)for(int candidate=0;candidate<c;candidate++){bool a=old.TryGet(node,candidate,out double ac),b=fast.TryGet(node,candidate,out double bc);Assert.AreEqual(a,b);if(a)Assert.AreEqual(ac,bc);}
            int steps=0;while(old.Status==NavSolveStatus.Pending||fast.Status==NavSolveStatus.Pending){Assert.Less(++steps,20000);old.Advance(1);fast.Advance(1);}
            foreach(var origin in new[]{At(-32,0,"bank/west"),At(32,-16)}){
                var sub=PartialSharedRouteTests.Subscription(origin,At(32,0));var a=new NavIncrementalSharedRoute(old,sub);var b=new NavIncrementalSharedRoute(fast,sub);steps=0;while(a.Status==NavSolveStatus.Pending||b.Status==NavSolveStatus.Pending){Assert.Less(++steps,20000);a.Advance(1);b.Advance(1);}Assert.AreEqual(a.Status,b.Status);Assert.AreEqual(NavSolveStatus.Ready,a.Status);PartialSharedRouteTests.Equal(a.Route,b.Route);a.ReleaseStorage();b.ReleaseStorage();
            }
            TestContext.WriteLine("NODE_SEED_PAID "+Newtonsoft.Json.JsonConvert.SerializeObject(new{N=n,W=w,C=c,OldSteps=oldSteps,NodeSteps=fastSteps,OldProbes=oldProbes,NodeProbes=fastProbes,Saved=oldSteps-fastSteps}));old.ReleaseStorage();fast.ReleaseStorage();
        }
        [Test] public void NewSeedCheckpointRejectsWrongProviderSkippedRowsAndWrongSurfacePrefix()
        {
            var graph=LayeredNavigationGraphCompiler.Compile(Provider(),Clearance,Profile,Limits);var region=Region();int cap=graph.Nodes.Count*3;var f=new NavSharedTerminalField(graph,region,cap);var invalid=f.Save();invalid.Node=1;Assert.Throws<ArgumentException>(()=>NavSharedTerminalField.Restore(graph,region,cap,invalid));
            for(int i=0;i<3;i++)f.Advance(1);Assert.AreEqual("SeedByNode",Phase(f));invalid=f.Save();invalid.Node=graph.Nodes.Count-1;Assert.Throws<ArgumentException>(()=>NavSharedTerminalField.Restore(graph,region,cap,invalid));invalid=f.Save();invalid.Node=graph.Nodes.First(n=>n.Location.SurfaceId!=region.Requested.SurfaceId).Id;invalid.Candidate=1;Assert.Throws<ArgumentException>(()=>NavSharedTerminalField.Restore(graph,region,cap,invalid));f.ReleaseStorage();
            var flatGraph=LayeredNavigationGraphCompiler.Compile(PartialSharedRouteTests.Provider(),Clearance,PartialSharedRouteTests.Profile,PartialSharedRouteTests.Limits);var loc=PartialSharedRouteTests.At(6,0);var flatRegion=new NavTerminalRegion("wrong-provider",1,loc,new[]{loc});var flat=new NavSharedTerminalField(flatGraph,flatRegion,flatGraph.Nodes.Count);invalid=flat.Save();invalid.Phase=3;Assert.Throws<ArgumentException>(()=>NavSharedTerminalField.Restore(flatGraph,flatRegion,flatGraph.Nodes.Count,invalid));flat.ReleaseStorage();
        }
        [Test] public void UnsafeCapAndZeroNodesRetainLegacyUninspectedSeedGrammar()
        {
            var graph=LayeredNavigationGraphCompiler.Compile(Provider(),Clearance,Profile,Limits);var region=Region();var cap=new NavSharedTerminalField(graph,region,1);Assert.AreEqual("Seed",Phase(cap));int steps=0;while(cap.Status==NavSolveStatus.Pending){Assert.Less(++steps,20000);cap.Advance(1);}Assert.AreEqual(NavSolveStatus.CapacityExceeded,cap.Status);cap.ReleaseStorage();
            var large=new NavClearanceProfile("zero",1,"ground",1,8);var sparse=new NavGraphProfile(new string('1',64),"distance",1,128);var zero=LayeredNavigationGraphCompiler.Compile(Provider(),large,sparse,Limits);Assert.Zero(zero.Nodes.Count);var at=At(ThreeCrossingsMap.Default.PocketX,0);Assert.True(zero.IsValid(at));var invalidCandidate=At(0,0);Assert.False(zero.IsValid(invalidCandidate));var empty=new NavSharedTerminalField(zero,new NavTerminalRegion("zero",1,at,new[]{invalidCandidate}),1);Assert.AreEqual("Seed",Phase(empty));empty.Advance(1);Assert.AreEqual(NavSolveStatus.Pending,empty.Status);empty.Advance(1);empty.Advance(1);Assert.AreEqual(NavSolveStatus.BlockedConnector,empty.Status);empty.ReleaseStorage();
        }
    }
    public sealed partial class NavigationPhysicalBodyTests
    {
        [TestCase("ValidateSeeds")][TestCase("WrongSurface")][TestCase("Matching")][TestCase("Search")]
        public void BodyNodeFirstSeedPhasesPreserveRawHeapColdNextAliasesAndRefusal(string point)
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var init));var r=init.OwnedReservation();var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(NodeFirstSeedTests.Provider(),NodeFirstSeedTests.Clearance,NodeFirstSeedTests.Profile,NodeFirstSeedTests.Limits,r);int steps=0;while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready){Assert.Less(++steps,20000);compiler.Advance(1);}compiler.SealPrepared();var field=NavSharedTerminalField.CopyRegionAndCreateOwned(compiler.Graph,NodeFirstSeedTests.Region(),compiler.Graph.Nodes.Count*3,r);
            bool AtPoint(){var state=field.Save();string phase=NodeFirstSeedTests.Phase(field);return point=="ValidateSeeds"?phase==point:point=="Search"?phase==point:phase=="SeedByNode"&&(point=="WrongSurface"?state.Candidate==0&&field.Graph.Nodes[state.Node].Location.SurfaceId!=field.Region.Requested.SurfaceId:state.Candidate==1);}
            while(!AtPoint()){Assert.Less(++steps,20000);field.Advance(1);}field.SealPrepared();var bundle=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{field,field},r.TryReserve);field.ReleaseStorage();compiler.ReleaseStorage();init.Attach(bundle);init.Commit(bundle,_=>{});var bytes=CaptureGraph(hot,bundle);long charge=hot.PhysicalBytes;
            byte[] Next(NavigationRetentionBudget budget,NavSharedTerminalField value){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,2,out var scope));var q=value.ForkOwned(scope.OwnedReservation());Assert.AreEqual(1,q.Advance(1).Consumed);q.SealPrepared();scope.Attach(q);scope.Commit(q,_=>{});var data=CaptureGraph(budget,q);q.ReleaseStorage();return data;}
            var expected=Next(hot,(NavSharedTerminalField)bundle[0]);var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreSame(restored[0],restored[1]);Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,(NavSharedTerminalField)restored[0]));
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Projection,1,3,out var refused));Assert.Throws<InvalidOperationException>(()=>((NavSharedTerminalField)bundle[0]).ForkOwned(new Refusal(refused,1)));refused.Discard();CollectionAssert.AreEqual(bytes,CaptureGraph(hot,bundle));Assert.AreEqual(charge,hot.PhysicalBytes);restored.Release();bundle.Release();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);Assert.Zero(hot.ActiveScopes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
        }
    }
}

using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PartialSharedRouteTests
    {
        internal static readonly NavClearanceProfile Clearance=new NavClearanceProfile("partial",1,"ground",1,.25);
        internal static readonly NavGraphProfile Profile=new NavGraphProfile(new string('d',64),"distance",1,2);
        internal static readonly NavGraphCompileLimits Limits=new NavGraphCompileLimits(3000,3000,20000);
        internal static NavLocation At(double x,double z,string surface="ground")=>new NavLocation(new NavPoint(x,z),surface);
        internal static LayeredNavigationProvider Provider()=>new LayeredNavigationProvider("partial",1,1,1,new string('e',64),new[]{new NavLayerSurface("ground","ground",0,1,new NavTraversalArea(new NavObstacle(-20,-20,20,20).Footprint,24,new[]{new NavObstacle(-.5,-1,.5,1)}))},Array.Empty<NavLayerPortal>());
        internal static NavRouteSubscription Subscription(NavLocation from,NavLocation to,int id=1)=>new NavRouteSubscription(1,1,1,1,0,1,id,1,1,1,id,from,to,null);
        internal static void Tick(NavSharedTerminalField field,NavIncrementalSharedRoute route)
        {
            if(field.Status==NavSolveStatus.Pending&&(!field.PartialCapacitySafe||!field.SeedComplete)){field.Advance(1);return;}
            if(route.AdvancePartial(1).Consumed==0&&field.Status==NavSolveStatus.Pending)field.AdvanceOne(route);
        }
        internal static void Equal(NavTypedRoute expected,NavTypedRoute actual)
        {
            Assert.AreEqual(expected.Cost,actual.Cost,"exact route sum order");Assert.AreEqual(expected.Legs.Count,actual.Legs.Count);
            for(int i=0;i<expected.Legs.Count;i++){var a=expected.Legs[i];var b=actual.Legs[i];Assert.AreEqual(a.From,b.From);Assert.AreEqual(a.To,b.To);Assert.AreEqual(a.Cost,b.Cost);Assert.AreEqual(a.Kind,b.Kind);Assert.AreEqual(a.PortalId,b.PortalId);}
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(true,true)]
        public void FullAndPartialKeepExactCandidateEgressTieTypedRouteAndNextOrigin(bool layered,bool closed)
        {
            var provider=layered?new StackedNavigationFixture(!closed).Provider:Provider();var graph=LayeredNavigationGraphCompiler.Compile(provider,Clearance,Profile,Limits);
            var endpoint=layered?At(3,2,"upper"):At(6,0);var origin=layered?At(-5,.25,"lower"):At(-6,0);
            var region=new NavTerminalRegion("partial-region",1,endpoint,layered?new[]{At(2,2,"upper"),At(2,0,"upper"),At(3,2,"upper")}:new[]{At(4,-2),At(4,2),At(8,4)});
            var full=new NavSharedTerminalField(graph,region,graph.Nodes.Count*region.Candidates.Count);int work=0;while(full.Status==NavSolveStatus.Pending){Assert.Less(++work,300000);full.Advance(17);}
            var partial=new NavSharedTerminalField(graph,region,graph.Nodes.Count*region.Candidates.Count);bool witnessed=false;
            foreach(var from in new[]{origin,layered?At(-6,-1,"lower"):At(-16,3)}){
                var sub=Subscription(from,endpoint);var expected=new NavIncrementalSharedRoute(full,sub);var actual=new NavIncrementalSharedRoute(partial,sub);work=0;
                while(expected.Status==NavSolveStatus.Pending){Assert.Less(++work,300000);expected.Advance(7);}
                work=0;while(actual.Status==NavSolveStatus.Pending){Assert.Less(++work,300000);Tick(partial,actual);}
                Assert.AreEqual(expected.Status,actual.Status);if(actual.Status==NavSolveStatus.Ready){Equal(expected.Route,actual.Route);witnessed|=partial.Status==NavSolveStatus.Pending;}
                expected.ReleaseStorage();actual.ReleaseStorage();
            }
            if(!layered)Assert.True(witnessed,"certified request must finish before full field");partial.ReleaseStorage();full.ReleaseStorage();
        }
        [Test] public void PartialCheckpointBeforeAndAfterCertificatePreservesFixedContinuation()
        {
            var graph=LayeredNavigationGraphCompiler.Compile(Provider(),Clearance,Profile,Limits);var endpoint=At(6,0);var region=new NavTerminalRegion("checkpoint",1,endpoint,new[]{At(4,-2),At(4,2)});int cap=graph.Nodes.Count*2;
            var field=new NavSharedTerminalField(graph,region,cap);var sub=Subscription(At(-6,0),endpoint);var route=new NavIncrementalSharedRoute(field,sub);int steps=0,restores=0;
            while(route.Status==NavSolveStatus.Pending){Assert.Less(++steps,300000);Tick(field,route);
                if(steps%97==0||Phase(route)=="Origin"){
                    var f=NavSharedTerminalField.Restore(graph,region,cap,field.Save());var r=NavIncrementalSharedRoute.Restore(f,sub,route.Save());
                    Assert.AreEqual(Phase(route),Phase(r));Tick(field,route);Tick(f,r);Assert.AreEqual(field.Status,f.Status);Assert.AreEqual(route.Status,r.Status);Assert.AreEqual(route.Save().Best,r.Save().Best);route.ReleaseStorage();field.ReleaseStorage();field=f;route=r;restores++;
                }
            }
            Assert.Greater(restores,1);Assert.AreEqual(NavSolveStatus.Ready,route.Status);Assert.AreEqual(NavSolveStatus.Pending,field.Status);route.ReleaseStorage();field.ReleaseStorage();
        }
        internal static string Phase(NavIncrementalSharedRoute route)=>typeof(NavIncrementalSharedRoute).GetField("phase",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(route).ToString();
        [Test] public void CapacityFallbackRetainsLateFailureAndOrdinaryPendingAdvance()
        {
            var graph=LayeredNavigationGraphCompiler.Compile(Provider(),Clearance,Profile,Limits);var endpoint=At(6,0);var region=new NavTerminalRegion("capacity",1,endpoint,new[]{endpoint});var field=new NavSharedTerminalField(graph,region,1);var route=new NavIncrementalSharedRoute(field,Subscription(At(-6,0),endpoint));Assert.False(field.PartialCapacitySafe);var restored=NavIncrementalSharedRoute.Restore(field,Subscription(At(-6,0),endpoint),route.Save());Assert.AreEqual(NavSolveStatus.Pending,restored.Status);restored.ReleaseStorage();Assert.Zero(route.AdvancePartial(10).Consumed);int steps=0;while(field.Status==NavSolveStatus.Pending){Assert.Less(++steps,300000);field.Advance(1);}Assert.AreEqual(NavSolveStatus.CapacityExceeded,field.Status);route.Advance(1);Assert.AreEqual(field.Status,route.Status);route.ReleaseStorage();field.ReleaseStorage();
        }
    }
    public sealed partial class NavigationPhysicalBodyTests
    {
        [TestCase(false)][TestCase(true)] public void BodyPartialSharedCertificateAliasesColdNextAndMissingIngressFailClosed(bool corrupt)
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var init));var r=init.OwnedReservation();
            var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(PartialSharedRouteTests.Provider(),PartialSharedRouteTests.Clearance,PartialSharedRouteTests.Profile,PartialSharedRouteTests.Limits,r);int steps=0;while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready){Assert.Less(++steps,300000);compiler.Advance(1);}compiler.SealPrepared();
            var endpoint=PartialSharedRouteTests.At(6,0);var field=NavSharedTerminalField.CopyRegionAndCreateOwned(compiler.Graph,new NavTerminalRegion("owned-partial",1,endpoint,new[]{PartialSharedRouteTests.At(4,-2),PartialSharedRouteTests.At(4,2)}),compiler.Graph.Nodes.Count*2,r);var route=NavIncrementalSharedRoute.CopySubscriptionAndCreateOwned(field,PartialSharedRouteTests.Subscription(PartialSharedRouteTests.At(-6,0),endpoint),r);
            steps=0;while(PartialSharedRouteTests.Phase(route)!="Origin"){Assert.Less(++steps,300000);PartialSharedRouteTests.Tick(field,route);}Assert.AreEqual(NavSolveStatus.Pending,field.Status);
            if(corrupt){var ingress=(NavCowDictionary<int,double>)typeof(NavIncrementalSharedRoute).GetField("ingress",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(route);Assert.Greater(ingress.Count,1);ingress.Remove(ingress.First().Key);}
            field.SealPrepared();route.SealPrepared();var bundle=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{field,route,route},r.TryReserve);route.ReleaseStorage();field.ReleaseStorage();compiler.ReleaseStorage();init.Attach(bundle);init.Commit(bundle,_=>{});var bytes=CaptureGraph(hot,bundle);long charge=hot.PhysicalBytes;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));
            if(corrupt){Assert.Throws<ArgumentException>(()=>Decode(bytes,decode.OwnedReservation()));decode.Discard();}
            else{
                var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreSame(restored[1],restored[2]);Assert.AreSame(restored[0],((NavIncrementalSharedRoute)restored[1]).PreparedField);Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                byte[] Next(NavigationRetentionBudget b,NavRetainedArray<NavOwnedStorage> state){Assert.True(b.TryOpenScope(NavigationPreparationKind.Projection,1,2,out var scope));var f=(NavSharedTerminalField)state[0];var q=((NavIncrementalSharedRoute)state[1]).ForkOwned(f,scope.OwnedReservation());q.AdvancePartial(1);q.SealPrepared();scope.Attach(q);scope.Commit(q,_=>{});var answer=CaptureGraph(b,q);q.ReleaseStorage();return answer;}
                CollectionAssert.AreEqual(Next(hot,bundle),Next(cold,restored));restored.Release();
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var refuse));Assert.Throws<InvalidOperationException>(()=>Decode(bytes,new Refusal(refuse,1)));refuse.Discard();
            }
            bundle.Release();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);Assert.Zero(hot.ActiveScopes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)] public void BodyPaidPartialFieldProbeKeepsChunkOneCreditAcrossColdEpochRefusalAndCancel(bool nextEpoch)
        {
            var source=NamedSchedulerSource(out var hot,out var first,out var second,true);const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            LayeredNavigationScheduler.Job Head(LayeredNavigationScheduler value)=>((NavPersistentQueue<LayeredNavigationScheduler.Job>)typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(value)).First;
            int steps=0;while(Head(source).Phase!=LayeredNavigationScheduler.JobPhase.PartialFieldProbePaid){
                Assert.Less(++steps,10000);Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,steps,out var scope));var fork=source.ForkOwned(scope.OwnedReservation());Assert.AreEqual(1,fork.Advance(1,new NavEpochQuota(10000,10000,10000,10000,10000),_=>true,1));fork.SealPrepared();scope.Attach(fork);scope.Commit(fork,_=>{});source.ReleaseStorage();source=fork;
            }
            var bytes=CaptureGraph(hot,source);long charge=hot.PhysicalBytes;var before=source.Counters;
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,20000,out var countScope));var counter=new Refusal(countScope,int.MaxValue);var trial=source.ForkOwned(counter);Assert.AreEqual(1,trial.Advance(nextEpoch?2:1,new NavEpochQuota(1,1,0,1,1),_=>true,1));int allocations=counter.Calls;trial.ReleaseStorage();countScope.Discard();
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,20001,out var rejected));LayeredNavigationScheduler failed=null;Assert.Throws<InvalidOperationException>(()=>{failed=source.ForkOwned(new Refusal(rejected,allocations));failed.Advance(nextEpoch?2:1,new NavEpochQuota(1,1,0,1,1),_=>true,1);});failed?.ReleaseStorage();rejected.Discard();Assert.AreEqual(charge,hot.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,source));Assert.AreEqual(LayeredNavigationScheduler.JobPhase.PartialFieldProbePaid,Head(source).Phase);
            byte[] Next(NavigationRetentionBudget b,LayeredNavigationScheduler value){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,30000,out var scope));var fork=value.ForkOwned(scope.OwnedReservation());Assert.AreEqual(1,fork.Advance(nextEpoch?2:1,new NavEpochQuota(1,1,0,1,1),_=>true,1));Assert.AreEqual(before.ConnectorWork,fork.Counters.ConnectorWork,"paid probe cannot be charged again");Assert.AreEqual(before.PhysicalFieldWork+1,fork.Counters.PhysicalFieldWork);Assert.AreEqual(LayeredNavigationScheduler.JobPhase.Connectors,Head(fork).Phase);fork.SealPrepared();scope.Attach(fork);scope.Commit(fork,_=>{});var result=CaptureGraph(b,fork);fork.ReleaseStorage();return result;}
            var expected=Next(hot,source);var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(LayeredNavigationScheduler)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored));restored.ReleaseStorage();
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,40000,out var cancelScope));var cancelled=source.ForkOwned(cancelScope.OwnedReservation());Assert.True(cancelled.Cancel(first.Subscription.StableKey));Assert.AreEqual(LayeredNavigationScheduler.JobPhase.Connectors,Head(cancelled).Phase);Assert.Null(Head(cancelled).Connector);cancelled.SealPrepared();cancelScope.Attach(cancelled);cancelScope.Discard();cancelled.ReleaseStorage();
            source.ReleaseStorage();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);Assert.Zero(hot.ActiveScopes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
        }
        [Test] public void BodyPaidPartialFieldPhaseRejectsUnpreparedForeignCredit()
        {
            var source=NamedSchedulerSource(out var hot,out _,out _,true);var jobs=(NavPersistentQueue<LayeredNavigationScheduler.Job>)typeof(LayeredNavigationScheduler).GetField("jobs",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(source);jobs.First.Phase=LayeredNavigationScheduler.JobPhase.PartialFieldProbePaid;var bytes=CaptureGraph(hot,source);var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));Assert.Throws<ArgumentException>(()=>Decode(bytes,decode.OwnedReservation()));decode.Discard();source.ReleaseStorage();Assert.Zero(hot.PhysicalBytes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
        }
    }
}

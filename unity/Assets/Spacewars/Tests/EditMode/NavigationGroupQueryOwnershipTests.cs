using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [TestCase(false)][TestCase(true)] public void GroupQueryGraphBorrowKeepsDistinctProvidersColdNextCowAndRefusal(bool changedHoldBinding)
        {
            var map=ThreeCrossingsMap.Default;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var rawA=new PartitionNavigationProvider(map,geometry,null);var rawB=new PartitionNavigationProvider(map,geometry,changedHoldBinding?"held-v2":null);
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('c',64),"distance",1,16);var limits=new NavGraphCompileLimits(2000,2000,10000);
            var sampled=LayeredNavigationGraphCompiler.Compile(rawA,clearance,profile,limits);var origin=sampled.Nodes[0].Location;var goal=sampled.Nodes.Last().Location;
            Assert.False(rawA.TryTrace(origin,goal.Position,clearance,out _,out _),"fixture needs a routed query, not direct completion");sampled.ReleaseOwner();
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var birth));var r=birth.OwnedReservation();
            var a=PartitionNavigationProvider.CopyOwned(rawA,r);var b=PartitionNavigationProvider.CopyOwned(rawB,r);Assert.AreNotSame(a,b);Assert.AreEqual(!changedHoldBinding,a.Binding==b.Binding);
            NavScheduledRequest Recipe(PartitionNavigationProvider raw,PartitionNavigationProvider owned,int entity){var sub=new NavRouteSubscription(1,9,1,1,0,1,entity,1,1,1,entity,origin,goal,raw.HoldBinding);return NavScheduledRequest.CopyOwned(new NavScheduledRequest(raw,clearance,profile,limits,new NavTerminalRegion("group-owner",1,goal,new[]{goal}),sub,4096,query:true,groupQuery:true),owned,r);}
            var first=Recipe(rawA,a,1);var second=Recipe(rawB,b,2);var source=LayeredNavigationScheduler.CreateOwned(r);source.BindAttemptArtifacts(NavigationArrivalTestFactory.Sha("group-owner"));Assert.True(source.Submit(first));Assert.True(source.Submit(second));first.StorageRoot.ReleaseOwner();second.StorageRoot.ReleaseOwner();a.StorageRoot.ReleaseOwner();b.StorageRoot.ReleaseOwner();source.SealPrepared();birth.Attach(source);birth.Commit(source,_=>{});
            int epoch=0;var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            LayeredNavigationScheduler.Job Head(LayeredNavigationScheduler s)=>((NavPersistentQueue<LayeredNavigationScheduler.Job>)typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(s)).First;
            void Step(bool take=false){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,++epoch,out var scope));var next=source.ForkOwned(scope.OwnedReservation());
                if(take){Assert.True(next.TryTake(_=>true,out var answer));try{Assert.AreEqual(NavSolveStatus.Ready,answer.Status);}finally{answer.ReleaseOwner();}}
                else next.Advance(epoch,new NavEpochQuota(128,128,128,128,256),_=>true,1);
                next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});source.ReleaseOwner();source=next;}
            try{
                while(source.ReadyCount==0){Assert.Less(epoch,20000);Step();}Assert.AreEqual(1,source.Counters.GraphBuilds);Step(true);
                while(Head(source).Singleton==null){Assert.Less(epoch,20000);Step();}
                Assert.AreEqual(changedHoldBinding?2:1,source.Counters.GraphBuilds);var head=Head(source);Assert.AreSame(second,head.Request);
                if(!changedHoldBinding){Assert.AreSame(first,head.GraphProviderOwner);Assert.AreSame(a,head.Graph.PhysicalProvider);Assert.AreNotSame(head.Request.Provider,head.Graph.PhysicalProvider);Assert.AreEqual(head.Request.Provider.Binding,head.Graph.PhysicalProvider.Binding);}
                else{Assert.AreSame(second,head.GraphProviderOwner);Assert.AreSame(b,head.Graph.PhysicalProvider);}
                byte[] bytes=CaptureGraph(budget,source);long charge=budget.PhysicalBytes;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,30000,out var refused));var refusal=new Refusal(refused,1);Assert.Throws<InvalidOperationException>(()=>source.ForkOwned(refusal));refused.Discard();Assert.AreEqual(charge,budget.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,source));Assert.Zero(budget.HeldBytes);
                byte[] Next(NavigationRetentionBudget ledger,LayeredNavigationScheduler current){Assert.True(ledger.TryOpenScope(NavigationPreparationKind.Solve,1,40000,out var scope));var next=current.ForkOwned(scope.OwnedReservation());next.Advance(epoch+1,new NavEpochQuota(3,3,3,3,3),_=>true,3);next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});var output=CaptureGraph(ledger,next);next.ReleaseOwner();return output;}
                var expected=Next(budget,source);source.ReleaseOwner();source=null;Assert.Zero(budget.PhysicalBytes);
                var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(LayeredNavigationScheduler)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
                try{Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));var restoredHead=Head(restored);if(!changedHoldBinding){Assert.AreNotSame(restoredHead.Request.Provider,restoredHead.Graph.PhysicalProvider);Assert.AreSame(restoredHead.GraphProviderOwner.Provider,restoredHead.Graph.PhysicalProvider);}CollectionAssert.AreEqual(expected,Next(cold,restored));}
                finally{restored.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);}
            }finally{source?.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
    }
}

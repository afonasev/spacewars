using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test] public void RetainedDirectQueryBodyColdNextCowRefusalAndLogicalAssociation()
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var birth));var r=birth.OwnedReservation();
            var terrain=ThreeCrossingsMap.Default;var provider=PartitionNavigationProvider.CopyOwned(new PartitionNavigationProvider(terrain,new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),terrain.Revision),null),r);
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.2);Assert.True(provider.TryLocate(new NavPoint(-18,0),clearance,out var origin));Assert.True(provider.TryLocate(new NavPoint(18,0),clearance,out var goal));
            var sub=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,origin,goal,null);
            var recipe=NavScheduledRequest.CopyOwned(new NavScheduledRequest(provider,clearance,new NavGraphProfile(new string('c',64),"distance",1,1),new NavGraphCompileLimits(10000,10000,40000),new NavTerminalRegion("direct-query",1,goal,new[]{goal}),sub,4096,query:true),provider,r);
            var source=LayeredNavigationScheduler.CreateOwned(r);Assert.True(source.Submit(recipe));recipe.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();
            // Prepare the exact continuation of the Direct phase's refused
            // publication using its production certificate and owned Job COW.
            // This tests the decoder boundary, not an observed game retention event.
            source.Advance(0,new NavEpochQuota(0,0,0,0,0),_=>true,0);
            var route=source.DirectPreparedRoute(recipe);Assert.NotNull(route);Assert.Greater(route.Legs.Count,1);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var jobs=(NavPersistentQueue<LayeredNavigationScheduler.Job>)typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(source);
            var job=jobs.First.Fork(r.TryReserve);job.DirectRoute=route;job.Phase=LayeredNavigationScheduler.JobPhase.Connectors;job.Seal();jobs.Replace(job.QueueNode,job);job.Release();route.ReleaseOwner();
            var saved=source.Save();Assert.AreEqual(1,saved.Jobs.Length);Assert.NotNull(saved.Jobs[0].DirectRoute);Assert.IsNull(saved.Jobs[0].DirectSubscription);Assert.Zero(source.ReadyCount);Assert.AreEqual(1,source.PendingCount);
            source.SealPrepared();birth.Attach(source);birth.Commit(source,_=>{});
            var bytes=CaptureGraph(hot,source);long charge=hot.PhysicalBytes;
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var refused));var refusal=new Refusal(refused,1);Assert.Throws<InvalidOperationException>(()=>source.ForkOwned(refusal));refused.Discard();Assert.AreEqual(charge,hot.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,source));Assert.Zero(hot.HeldBytes);Assert.Zero(hot.ActiveScopes);
            var malformed=source.Save();malformed.Jobs[0].Phase=(int)LayeredNavigationScheduler.JobPhase.Direct;Assert.Throws<ArgumentException>(()=>LayeredNavigationScheduler.Restore(malformed,new[]{recipe}));
            var logical=LayeredNavigationScheduler.Restore(source.Save(),new[]{recipe});Assert.AreEqual(1,logical.PendingCount);logical.ClearWorld();logical.ReleaseOwner();
            byte[] Next(NavigationRetentionBudget budget,LayeredNavigationScheduler current){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var scope));var next=current.ForkOwned(scope.OwnedReservation());next.Advance(1,new NavEpochQuota(1,1,1,1,0),_=>true,1);Assert.AreEqual(1,next.ReadyCount);next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});var result=CaptureGraph(budget,next);next.ReleaseOwner();return result;}
            var expected=Next(hot,source);source.ReleaseOwner();Assert.Zero(hot.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(LayeredNavigationScheduler)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(expected,Next(cold,restored));restored.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
        }
    }
}

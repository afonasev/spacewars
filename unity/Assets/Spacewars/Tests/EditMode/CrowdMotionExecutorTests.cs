using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdMotionExecutorTests
    {
        private sealed class SkewedWork:INavMotionWork,IDisposable
        {
            private int cursor=-1;
            internal readonly int[] Visits=new int[200],Threads=new int[200];
            private readonly CountdownEvent entered=new CountdownEvent(3);
            public void RunRows(){int row;while((row=Interlocked.Increment(ref cursor))<Visits.Length){
                Threads[row]=Thread.CurrentThread.ManagedThreadId;Interlocked.Increment(ref Visits[row]);
                // Only the first three rows are expensive. Fixed contiguous
                // chunks would put them all on one worker.
                if(row<3){entered.Signal();if(!entered.Wait(3000))throw new TimeoutException("Skewed rows were not shared.");}
            }}
            public void Dispose()=>entered.Dispose();
        }
        [Test] public void ClusteredExpensiveRowsAreStolenExactlyOnceAndExecutorClearsJobsBeforeStop()
        {
            var executor=new NavMotionExecutor(2);
            try{for(int repeat=0;repeat<3;repeat++)using(var work=new SkewedWork()){
                executor.Dispatch(work);Assert.True(work.Visits.All(n=>n==1));Assert.AreEqual(3,work.Threads.Take(3).Distinct().Count());Assert.True(executor.IsIdle);
            }}finally{executor.Dispose();}
            Assert.True(executor.IsIdle);using(var work=new SkewedWork())Assert.Throws<ObjectDisposedException>(()=>executor.Dispatch(work));
        }
        private static byte[] State(NavigationSession session){using(var state=session.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        private static NavigationSession Create()
        {
            var map=ThreeCrossingsMap.Default;var session=new NavigationSession(71,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),PlayableProfile.Default.Navigation,terrain:map);
            for(int x=0;x<8;x++)for(int z=0;z<5;z++){
                var point=new NavPoint(-55+x*4.5,-20+z*4.5);var goal=new NavPoint(point.X+2,point.Z);
                if(!map.TryLocate(point,.72,out var location)||!map.TryTraverse(location,goal,.72,out _))continue;
                int id=session.Crowd.Units.Count+1;session.Crowd.Add(id,point,.72,4,4.5);Assert.True(session.Crowd.SetRoute(id,goal,new[]{goal}));
            }
            Assert.Greater(session.Crowd.Units.Count,16);return session;
        }
        [Test] public void PreparedSerialAndConcurrentMovementKeepExactHeadersProposalsAndPaidSource()
        {
            var session=Create();var previous=NavMotionExecutor.Current;bool previousBitmap=TerrainLocations.DisableCutBitmapForTests;
            try{using(var executor=new NavMotionExecutor(3)){
                var before=State(session);long paid=session.PhysicalHostBudget.PhysicalBytes;
                NavUnit[] expected=null;NavMotionProposal[] proposals=null;int neighbors=-1;
                foreach(int mode in new[]{0,1,2,1,0}){
                    TerrainLocations.DisableCutBitmapForTests=mode==0;NavMotionExecutor.Current=mode==0?null:executor;long batches=executor.CompletedBatches;int refused=0;
                    Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,0,out var scope));
                    using(scope){var frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,scope.OwnedReservation());
                        try{frame.Observe();
                            if(mode==2){long charge=NavStorageScratch.LeaseBytes+(executor.HelperCount+1L)*frame.Working.ParallelSelectScratchBound;session.PhysicalHostBudget.AllocationAdmission=bytes=>{if(bytes!=charge)return true;refused++;return false;};}
                            frame.Compute(1d/30);Assert.True(frame.SourceCurrent());
                            if(mode==2){Assert.AreEqual(1,refused);Assert.AreEqual(batches,executor.CompletedBatches,"Refused scratch must retain serial selection");}else if(mode==1)Assert.AreEqual(batches+1,executor.CompletedBatches);
                            if(expected==null){expected=frame.After.Select(u=>u.CopyMovementHeader()).ToArray();proposals=frame.Proposals.ToArray();neighbors=frame.Working.NeighborCandidates;}
                            else{for(int i=0;i<expected.Length;i++){Assert.True(expected[i].SameMovementValues(frame.After[i]),"row "+i);AssertProposal(proposals[i],frame.Proposals[i]);}Assert.AreEqual(neighbors,frame.Working.NeighborCandidates);}
                        }finally{session.PhysicalHostBudget.AllocationAdmission=null;frame.Release();}
                    }
                    Assert.True(executor.IsIdle);CollectionAssert.AreEqual(before,State(session));Assert.AreEqual(paid,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);
                }
            }}finally{TerrainLocations.DisableCutBitmapForTests=previousBitmap;NavMotionExecutor.Current=previous;session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);}
        }
        private static void AssertProposal(NavMotionProposal a,NavMotionProposal b)
        {
            double[] left={a.Position.X,a.Position.Z,a.Velocity.X,a.Velocity.Z,a.Heading},right={b.Position.X,b.Position.Z,b.Velocity.X,b.Velocity.Z,b.Heading};
            for(int i=0;i<left.Length;i++)Assert.AreEqual(BitConverter.DoubleToInt64Bits(left[i]),BitConverter.DoubleToInt64Bits(right[i]));
            Assert.AreEqual(a.Blocked,b.Blocked);Assert.AreEqual(a.Overflow,b.Overflow);Assert.AreEqual(a.Refuse,b.Refuse);Assert.AreEqual(a.StaticNoWinner,b.StaticNoWinner);Assert.AreEqual(a.IndependentTurn,b.IndependentTurn);
        }
        [Test] public void ConcurrentFailureJoinsAllRowsAndRethrowsLowestRowWithoutSourceMutation()
        {
            var session=Create();var previous=NavMotionExecutor.Current;
            try{using(var executor=new NavMotionExecutor(2))using(var higherFailed=new ManualResetEvent(false)){
                var before=State(session);long paid=session.PhysicalHostBudget.PhysicalBytes;var first=new InvalidOperationException("first row");var later=new InvalidOperationException("later row");int completed=0;
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,session.Generation,0,out var scope));
                using(scope){var frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,scope.OwnedReservation());
                    try{frame.Observe();for(int i=0;i<frame.Motion.Length;i++)frame.Motion[i]=new NavMotionBody{Active=true,Speed=1};
                        frame.SelectRowForTests=row=>{Interlocked.Increment(ref completed);if(row==0){if(!higherFailed.WaitOne(3000))throw new TimeoutException();throw first;}higherFailed.Set();throw later;};
                        NavMotionExecutor.Current=executor;Assert.AreSame(first,Assert.Throws<InvalidOperationException>(()=>frame.TrySelectParallel(1d/30)));Assert.AreEqual(frame.Motion.Length,completed);Assert.True(executor.IsIdle);Assert.True(frame.SourceCurrent());
                    }finally{frame.Release();}
                }
                CollectionAssert.AreEqual(before,State(session));Assert.AreEqual(paid,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);
            }}finally{NavMotionExecutor.Current=previous;session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);}
        }
    }
}

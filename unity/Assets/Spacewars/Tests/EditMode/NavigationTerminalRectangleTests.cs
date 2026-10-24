using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavScheduledRequest WithTerminalRectangle(NavScheduledRequest r,bool corners=false)=>new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,r.MaxLabels,query:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true,rectangularOrigins:true,terminalRectangleProof:true,terminalCornerProof:corners);
        private static NavScheduledRequest TerminalRequest()
        {
            var r=CertifiedRequest();var p=(PartitionNavigationProvider)r.Provider;var c=r.Clearance;
            Assert.True(p.TryLocate(new NavPoint(160,0),c,out var to));
            return new NavScheduledRequest(p,c,r.Profile,r.Limits,new NavTerminalRegion("terminal",1,to,new[]{to,new NavLocation(new NavPoint(160,1),to.SurfaceId),new NavLocation(new NavPoint(160,-1),to.SurfaceId)}),new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,r.Subscription.Origin,to,null),r.MaxLabels,query:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true,rectangularOrigins:true);
        }
        [Test] public void TerminalRectangleStrictBinary64SoundnessAndFallback()
        {
            var obstacle=new NavObstacle(-40,72,40,124);var from=new NavPoint(-40.8,108);var box=new NavHeuristicBox{MinX=59,MaxX=70,MinZ=55,MaxZ=67};
            Assert.True(NavTerminalRectangleProof.Reject(from,box,.25,obstacle));
            var geometry=new NavGeometry(240,new[]{obstacle},1);
            for(int x=0;x<=32;x++)for(int z=0;z<=32;z++){var to=new NavPoint(box.MinX+(box.MaxX-box.MinX)*x/32,box.MinZ+(box.MaxZ-box.MinZ)*z/32);Assert.False(geometry.SegmentFree(from,to,.25));}
            var point=new NavHeuristicBox{MinX=60,MaxX=60,MinZ=72,MaxZ=72};
            Assert.False(NavTerminalRectangleProof.Reject(new NavPoint(-60,72),point,0,obstacle),"collinear");
            Assert.False(NavTerminalRectangleProof.Reject(new NavPoint(-60,92),new NavHeuristicBox{MinX=60,MaxX=60,MinZ=-28,MaxZ=-28},0,obstacle),"corner tangent");
            Assert.False(NavTerminalRectangleProof.Reject(from,new NavHeuristicBox{MinX=-80,MaxX=70,MinZ=55,MaxZ=150},.25,obstacle),"outside shadow");
            Assert.False(NavTerminalRectangleProof.Reject(from,box,.25,new NavObstacle(new NavPoint(0,100),40)),"circle");
            Assert.False(NavTerminalRectangleProof.Reject(from,box,.25,new NavObstacle(obstacle.Footprint)),"polygon");
            box.MinX=double.NaN;Assert.False(NavTerminalRectangleProof.Reject(from,box,.25,obstacle));
            box.MinX=double.NegativeInfinity;Assert.False(NavTerminalRectangleProof.Reject(from,box,.25,obstacle));
            Assert.False(NavTerminalRectangleProof.Reject(new NavPoint(double.MaxValue,108),point,.25,obstacle),"overflow uncertain");
            Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(typeof(NavDemandQueryWork)),2048);
        }
        [Test] public void TerminalCornerAllCornersBinary64AndFallback()
        {
            var actual=new NavHeuristicBox{MinX=53.6,MaxX=82.4,MinZ=49.6,MaxZ=74.8};var obstacle=new NavObstacle(-40,72,40,124);
            Assert.False(NavTerminalRectangleProof.Reject(new NavPoint(-44,126.4),actual,.25,obstacle));
            Assert.True(NavTerminalRectangleProof.Reject(new NavPoint(-44,126.4),actual,.25,obstacle,true));
            // Captured sample2, mirrored over both axes to exercise all corners.
            for(int sx=-1;sx<=1;sx+=2)for(int sz=-1;sz<=1;sz+=2)
            {
                var r=new NavObstacle(-40,-26,40,26);var from=new NavPoint(-44*sx,28.4*sz);
                var box=new NavHeuristicBox{MinX=sx==1?53.6:-82.4,MaxX=sx==1?82.4:-53.6,MinZ=sz==1?-48.4:23.2,MaxZ=sz==1?-23.2:48.4};
                Assert.False(NavTerminalRectangleProof.Reject(from,box,0,r),"V10 corner miss remains exact");
                Assert.True(NavTerminalRectangleProof.Reject(from,box,0,r,true),"V11 corner proof");
                var geometry=new NavGeometry(240,new[]{r},1);
                for(int x=0;x<=32;x++)for(int z=0;z<=32;z++)Assert.False(geometry.SegmentFree(from,new NavPoint(box.MinX+(box.MaxX-box.MinX)*x/32,box.MinZ+(box.MaxZ-box.MinZ)*z/32),0));
                Assert.False(NavTerminalRectangleProof.Reject(from,box,0,new NavObstacle(r.Footprint),true));
                Assert.False(NavTerminalRectangleProof.Reject(from,box,0,new NavObstacle(new NavPoint(0,0),26),true));
                box.MinX=double.NaN;Assert.False(NavTerminalRectangleProof.Reject(from,box,0,r,true));
            }
            var square=new NavObstacle(-1,-1,1,1);var point=new NavHeuristicBox{MinX=2,MaxX=2,MinZ=-2,MaxZ=-2};
            Assert.True(NavTerminalRectangleProof.Reject(new NavPoint(-2,2),point,0,square,true),"shared Cross exactly zero; raw bbox includes corner");
            point.MinX=point.MaxX=-1.5;point.MinZ=point.MaxZ=1.5;
            Assert.False(NavTerminalRectangleProof.Reject(new NavPoint(-2,2),point,0,square,true),"same ray but corner outside raw segment bbox");
            point=new NavHeuristicBox{MinX=2,MaxX=2,MinZ=2,MaxZ=2};
            Assert.False(NavTerminalRectangleProof.Reject(new NavPoint(-2,2),point,0,square,true),"outside shadow");
            Assert.False(NavTerminalRectangleProof.Reject(new NavPoint(double.MaxValue,double.MaxValue),point,0,square,true),"nonfinite rounded cross fallback");
            Assert.False(NavTerminalRectangleProof.Reject(new NavPoint(-2,2),point,double.PositiveInfinity,square,true));
        }
        [TestCase(false)][TestCase(true)] public void TerminalRectangleExactObjectiveAndTieMatchOldPolicy(bool corners)
        {
            var old=corners?WithTerminalRectangle(TerminalRequest()):TerminalRequest();var fresh=WithTerminalRectangle(old,corners);Assert.AreEqual(corners,old.TerminalRectangleProof);Assert.True(fresh.TerminalRectangleProof);Assert.AreNotEqual(old.CompileKey,fresh.CompileKey);
            var a=new NavDemandQueryWork(old);var b=new NavDemandQueryWork(fresh);
            try{LazyComplete(a);LazyComplete(b);Assert.AreEqual(a.ExactObjective,b.ExactObjective);Assert.AreEqual(LazyField(a,"chosen"),LazyField(b,"chosen"));Assert.AreEqual(LazyField(a,"chosenCandidate"),LazyField(b,"chosenCandidate"));CollectionAssert.AreEqual(a.Save().Starts,b.Save().Starts);}
            finally{a.ReleaseOwner();b.ReleaseOwner();}
        }
        [TestCase(false)][TestCase(true)]
        public void TerminalRectangleQueryColdNextCowRefusalAndLogical(bool corners)
        {
            var raw=WithTerminalRectangle(TerminalRequest(),corners);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));NavDemandQueryWork hot=null;
            using(birth){var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,birth.OwnedReservation());var q=NavScheduledRequest.CopyOwned(raw,p,birth.OwnedReservation());hot=new NavDemandQueryWork(q,birth.TryReserve);q.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();
                for(int n=0;;n++){Assert.Less(n,10000);bool reached=(int)LazyField(hot,"terminalBlocker")>=0;if(reached)break;Assert.AreEqual(NavSolveStatus.Pending,hot.Status);V3Step(hot);}hot.Seal();birth.Attach(hot);birth.Commit(hot,_=>{});}
            byte[] bytes=CaptureGraph(budget,hot);var state=hot.Save();
            byte[] Next(NavigationRetentionBudget b,NavDemandQueryWork source){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));NavDemandQueryWork next=null;try{using(scope){next=source.Fork(scope.TryReserve);V3Step(next);next.Seal();scope.Attach(next);scope.Commit(next,_=>{});}return CaptureGraph(b,next);}finally{next?.ReleaseOwner();}}
            var expected=Next(budget,hot);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var probe));var count=new Refusal(probe,int.MaxValue);NavDemandQueryWork trial=null;try{trial=hot.Fork(count.TryReserve);V3Step(trial);}finally{trial?.ReleaseOwner();probe.Discard();}
            for(int n=1;n<=count.Calls;n++){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,n+10,out var scope));var refusal=new Refusal(scope,n);NavDemandQueryWork next=null;try{Assert.Throws<InvalidOperationException>(()=>{next=hot.Fork(refusal.TryReserve);V3Step(next);});}finally{next?.ReleaseOwner();scope.Discard();}CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));Assert.Zero(budget.HeldBytes);}
            hot.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavDemandQueryWork restored;using(decode){restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
            try{CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored));}finally{restored.ReleaseOwner();}Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
            var logical=NavDemandQueryWork.Restore(raw,state);try{V3Step(logical);Assert.True(logical.Matches(raw));}finally{logical.ReleaseOwner();}
        }
        [TestCase(false)][TestCase(true)]
        public void OriginShadowQueryColdBracketJumpCowRefusalAndLogical(bool jump)
        {
            var r=TerminalRequest();var raw=new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,r.MaxLabels,query:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true,rectangularOrigins:true,terminalRectangleProof:true,terminalCornerProof:true,originShadowProof:true);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));NavDemandQueryWork hot=null;
            using(birth){var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,birth.OwnedReservation());var q=NavScheduledRequest.CopyOwned(raw,p,birth.OwnedReservation());hot=new NavDemandQueryWork(q,birth.TryReserve);q.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();
                for(int n=0;;n++){Assert.Less(n,10000);bool reached=(int)LazyField(hot,"shadowBlocker")>=0&&(int)LazyField(hot,"shadowLow")>(int)LazyField(hot,"shadowStart")&&(!jump||(int)LazyField(hot,"shadowLow")== (int)LazyField(hot,"shadowHigh"));if(reached)break;Assert.AreEqual(NavSolveStatus.Pending,hot.Status);V3Step(hot);}hot.Seal();birth.Attach(hot);birth.Commit(hot,_=>{});}
            byte[] bytes=CaptureGraph(budget,hot);var state=hot.Save();byte[] expectedLogicalNext=null;
            byte[] Next(NavigationRetentionBudget b,NavDemandQueryWork source){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));NavDemandQueryWork next=null;try{using(scope){next=source.Fork(scope.TryReserve);V3Step(next);using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))next.WriteSeamContinuation(writer);expectedLogicalNext=stream.ToArray();}next.Seal();scope.Attach(next);scope.Commit(next,_=>{});}return CaptureGraph(b,next);}finally{next?.ReleaseOwner();}}
            var expected=Next(budget,hot);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var probe));var count=new Refusal(probe,int.MaxValue);NavDemandQueryWork trial=null;try{trial=hot.Fork(count.TryReserve);V3Step(trial);}finally{trial?.ReleaseOwner();probe.Discard();}
            for(int n=1;n<=count.Calls;n++){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,n+10,out var scope));var refusal=new Refusal(scope,n);NavDemandQueryWork next=null;try{Assert.Throws<InvalidOperationException>(()=>{next=hot.Fork(refusal.TryReserve);V3Step(next);});}finally{next?.ReleaseOwner();scope.Discard();}CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));Assert.Zero(budget.HeldBytes);}
            hot.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavDemandQueryWork restored;using(decode){restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
            try{CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored));}finally{restored.ReleaseOwner();}Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
            var logical=NavDemandQueryWork.Restore(raw,state);try{V3Step(logical);Assert.True(logical.Matches(raw));using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))logical.WriteSeamContinuation(writer);CollectionAssert.AreEqual(expectedLogicalNext,stream.ToArray(),"Exact logical next transcript must match hot/cold step");}CollectionAssert.AreEqual(state.Starts,logical.Save().Starts);CollectionAssert.AreEqual(state.Ends,logical.Save().Ends);}finally{logical.ReleaseOwner();}
        }
        [TestCase(false)][TestCase(true)]
        public void TerminalCostPendingColdNextCowRefusalAndLogical(bool reject)
        {
            var r=TerminalRequest();var raw=new NavScheduledRequest(r.Provider,r.Clearance,reject?new NavGraphProfile(r.Profile.ExactProfileBinding,"distance",1,40):r.Profile,reject?new NavGraphCompileLimits(169,120,480):r.Limits,r.Region,r.Subscription,reject?169:r.MaxLabels,query:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true,rectangularOrigins:true,terminalRectangleProof:true,terminalCornerProof:true,originShadowProof:true,terminalCostProof:true);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));NavDemandQueryWork hot=null;
            using(birth){var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,birth.OwnedReservation());var q=NavScheduledRequest.CopyOwned(raw,p,birth.OwnedReservation());hot=new NavDemandQueryWork(q,birth.TryReserve);q.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();
                for(int n=0;;n++){Assert.Less(n,10000);bool reached=(int)LazyField(hot,"terminalBlocker")==-2&&(bool)typeof(NavDemandQueryWork).GetMethod("TerminalCostRejects",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(hot,null)==reject;if(reached)break;Assert.AreEqual(NavSolveStatus.Pending,hot.Status);V3Step(hot);}hot.Seal();birth.Attach(hot);birth.Commit(hot,_=>{});}
            byte[] bytes=CaptureGraph(budget,hot);var state=hot.Save();byte[] expectedLogicalNext=null;
            byte[] Next(NavigationRetentionBudget b,NavDemandQueryWork source){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));NavDemandQueryWork next=null;try{using(scope){next=source.Fork(scope.TryReserve);V3Step(next);using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))next.WriteSeamContinuation(writer);expectedLogicalNext=stream.ToArray();}next.Seal();scope.Attach(next);scope.Commit(next,_=>{});}return CaptureGraph(b,next);}finally{next?.ReleaseOwner();}}
            var expected=Next(budget,hot);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var probe));var count=new Refusal(probe,int.MaxValue);NavDemandQueryWork trial=null;try{trial=hot.Fork(count.TryReserve);V3Step(trial);}finally{trial?.ReleaseOwner();probe.Discard();}
            for(int n=1;n<=count.Calls;n++){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,n+10,out var scope));var refusal=new Refusal(scope,n);NavDemandQueryWork next=null;try{Assert.Throws<InvalidOperationException>(()=>{next=hot.Fork(refusal.TryReserve);V3Step(next);});}finally{next?.ReleaseOwner();scope.Discard();}CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));Assert.Zero(budget.HeldBytes);}
            hot.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavDemandQueryWork restored;using(decode){restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
            try{CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored));}finally{restored.ReleaseOwner();}Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
            var logical=NavDemandQueryWork.Restore(raw,state);try{V3Step(logical);Assert.True(logical.Matches(raw));using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))logical.WriteSeamContinuation(writer);CollectionAssert.AreEqual(expectedLogicalNext,stream.ToArray(),"Exact logical next transcript must match hot/cold step");}CollectionAssert.AreEqual(state.Starts,logical.Save().Starts);CollectionAssert.AreEqual(state.Ends,logical.Save().Ends);}finally{logical.ReleaseOwner();}
        }
        [Test] public void TerminalCostProductionBinary64BoundAndStrictTies()
        {
            double big=NavExactHeuristic.Power(200),tiny=NavExactHeuristic.Power(-498),next=BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(big)-1);
            var cases=new[]{new[]{new NavPoint(0,0),new NavPoint(0,0),new NavPoint(0,0)},new[]{new NavPoint(0,0),new NavPoint(5,0),new NavPoint(10,0)},new[]{new NavPoint(-big,big),new NavPoint(0,-big),new NavPoint(big,-big)},new[]{new NavPoint(big,next),new NavPoint(next,big),new NavPoint(next,next)},new[]{new NavPoint(0,0),new NavPoint(double.Epsilon,tiny),new NavPoint(tiny,0)},new[]{new NavPoint(-12,32),new NavPoint(17,19),new NavPoint(68,64)}};
            foreach(var c in cases){Assert.True(NavExactHeuristic.TryTerminalBound(c[0],c[2],true,NavDistanceMetricContract.ManagedBinary64SqrtV1,out var lower));var exact=NavExactCost.FromDouble(LayeredNavigationProvider.Distance(c[0],c[1])).Add(LayeredNavigationProvider.Distance(c[1],c[2]));Assert.LessOrEqual(lower.CompareTo(exact),0);Assert.False(lower>lower,"Exact equality never rejects");Assert.True(lower.Add(double.Epsilon)>lower,"One exact accumulator quantum is strictly greater");}
            Assert.False(NavExactHeuristic.TryTerminalBound(new NavPoint(0,0),new NavPoint(1,1),false,NavDistanceMetricContract.ManagedBinary64SqrtV1,out _));Assert.False(NavExactHeuristic.TryTerminalBound(new NavPoint(0,0),new NavPoint(1,1),true,NavDistanceMetricContract.Unknown,out _));Assert.False(NavExactHeuristic.TryTerminalBound(new NavPoint(double.PositiveInfinity,0),new NavPoint(1,1),true,NavDistanceMetricContract.ManagedBinary64SqrtV1,out _));Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(typeof(NavDemandQueryWork)),2048);
        }

    }
}

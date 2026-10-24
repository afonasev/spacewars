using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavScheduledRequest OriginRectangleRequest()
        {
            var raw=CertifiedRequest();var p=(PartitionNavigationProvider)raw.Provider;var map=new CampaignOpeningMap();
            var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,p.ExclusionObstacles.Concat(new[]{new NavObstacle(-240,-160,-80,80),new NavObstacle(-240,-80,-80,160)}).ToArray(),map.Revision),null);
            return new NavScheduledRequest(provider,raw.Clearance,raw.Profile,raw.Limits,raw.Region,raw.Subscription,raw.MaxLabels,query:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true);
        }
        private static NavScheduledRequest WithRectangleOrigins(NavScheduledRequest r)=>new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,r.MaxLabels,query:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true,rectangularOrigins:true);
        [Test] public void RectangleOriginsExactMinimumAndFullIdsMatchPreviousCertifiedPolicy()
        {
            var old=CertifiedRequest();var current=WithRectangleOrigins(old);Assert.True(current.RectangularOrigins);Assert.AreNotEqual(old.CompileKey,current.CompileKey);
            var a=new NavDemandQueryWork(old);var b=new NavDemandQueryWork(current);
            try{for(int n=0;LazyField(a,"phase").ToString()!="Seed";n++){Assert.Less(n,10000);V3Step(a);}for(int n=0;LazyField(b,"phase").ToString()!="Seed";n++){Assert.Less(n,10000);V3Step(b);}CollectionAssert.AreEqual(a.Save().Starts,b.Save().Starts);Assert.True(b.Save().Starts.Any(v=>v.Key>=current.Limits.MaxNodes));LazyComplete(a);LazyComplete(b);Assert.AreEqual(a.ExactObjective,b.ExactObjective);}
            finally{a.ReleaseOwner();b.ReleaseOwner();}
        }
        [Test] public void OriginRectanglePaidRowsSkipOnlyImpossibleCellsAndRestoreLogical()
        {
            var q=OriginRectangleRequest();var w=new NavOriginRectangleWork(q);var visited=new bool[49];int paid=0;
            try{
                while(!w.Prepared){Assert.Less(++paid,1000);w.StepPreparation();}
                for(int x=0;x<7;x++){int z=0;for(;;){Assert.Less(++paid,2000);if(w.NeedsRowStep(x,z)){w.StepRow(x,z,0,6,out int next,out bool done);z=next;if(done)break;}else{Assert.LessOrEqual(z,6);visited[x*7+z]=true;z++;}}}
                var p=(PartitionNavigationProvider)q.Provider;
                for(int id=0;id<49;id++)if(p.TryLocate(new NavPoint((-3+id/7)*80,(-3+id%7)*80),q.Clearance,out _))Assert.True(visited[id],"Every feasible Origins node remains: "+id);
                Assert.AreEqual(28,visited.Count(v=>v));w.Validate();
                var restored=NavOriginRectangleWork.Restore(q,w.Save());try{restored.Validate();Assert.True(restored.Prepared);}finally{restored.ReleaseOwner();}
            }finally{w.ReleaseOwner();}
        }
        [TestCase("binary")][TestCase("rowbuild")][TestCase("skip")]
        public void OriginRectangleColdNextCowRefusalAndLogical(string target)
        {
            var raw=OriginRectangleRequest();var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));NavOriginRectangleWork hot;int z=0;
            using(birth){var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,birth.OwnedReservation());var q=NavScheduledRequest.CopyOwned(raw,p,birth.OwnedReservation());hot=new NavOriginRectangleWork(q,birth.TryReserve);q.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();
                if(target=="binary"){for(int i=0;;i++){Assert.Less(i,1000);if((int)LazyField(hot,"phase")==1&&((NavRectangleCursor)LazyField(hot,"cursor")).Work.Stage==NavExclusionStage.MinX)break;hot.StepPreparation();}}
                else{while(!hot.Prepared)hot.StepPreparation();hot.StepRow(0,0,0,6,out _,out _);hot.StepRow(0,0,0,6,out _,out _);if(target=="skip"){while((int)LazyField(hot,"rowPhase")!=2)hot.StepRow(0,0,0,6,out _,out _);hot.StepRow(0,0,0,6,out z,out _);Assert.Greater(((NavCowDictionary<int,int>)LazyField(hot,"intervals")).Count,0,"Persist overlapping intervals after first skip");}}
                hot.Seal();birth.Attach(hot);birth.Commit(hot,_=>{});}
            var bytes=CaptureGraph(budget,hot);var state=hot.Save();
            void Action(NavOriginRectangleWork n){if(target=="binary")n.StepPreparation();else n.StepRow(0,z,0,6,out _,out _);}
            byte[] Next(NavigationRetentionBudget b,NavOriginRectangleWork source){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));NavOriginRectangleWork n=null;try{using(scope){n=source.Fork(scope.TryReserve);Action(n);n.Seal();scope.Attach(n);scope.Commit(n,_=>{});}return CaptureGraph(b,n);}finally{n?.ReleaseOwner();}}
            var expected=Next(budget,hot);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var probe));var count=new Refusal(probe,int.MaxValue);NavOriginRectangleWork trial=null;try{trial=hot.Fork(count.TryReserve);Action(trial);}finally{trial?.ReleaseOwner();probe.Discard();}
            for(int nth=1;nth<=count.Calls;nth++){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,nth+10,out var scope));var refusal=new Refusal(scope,nth);NavOriginRectangleWork n=null;try{Assert.Throws<InvalidOperationException>(()=>{n=hot.Fork(refusal.TryReserve);Action(n);});}finally{n?.ReleaseOwner();scope.Discard();}CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));Assert.Zero(budget.HeldBytes);}
            hot.ReleaseOwner();PrefixNoLeaks(budget);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavOriginRectangleWork restored;using(decode){restored=(NavOriginRectangleWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
            try{CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored));}finally{restored.ReleaseOwner();}PrefixNoLeaks(cold);
            var logical=NavOriginRectangleWork.Restore(raw,state);try{Action(logical);logical.Validate();}finally{logical.ReleaseOwner();}
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    // Explicit flat-mode continuations: paid lanes, admission and exact owned state.
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static LayeredNavigationScheduler FlatPrimitive(LayeredNavigationScheduler source,NavigationRetentionBudget budget,long epoch,NavEpochQuota quota)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch+100,out var scope));
            try{var next=source.ForkOwned(scope.OwnedReservation());next.Advance(epoch,quota,_=>true,1);next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});source.ReleaseStorage();return next;}
            finally{scope.Discard();}
        }
        private static LayeredNavigationScheduler.Job FlatHead(LayeredNavigationScheduler root)
            =>((NavPersistentQueue<LayeredNavigationScheduler.Job>)typeof(LayeredNavigationScheduler).GetField("jobs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(root)).First;

        [TestCase(1d)][TestCase(.8d)] public void FlatN1EveryPaidPrimitiveColdBodyExactAndNextOperation(double grid)
        {
            var source=NamedSchedulerSource(out var hot,out _,out _,singleton:true,grid:grid);bool catalog=false,probe=false,relax=false,ready=false;
            try{
                for(int n=0;n<6000&&!ready;n++){
                    var work=FlatHead(source)?.Singleton;
                    if(work?.IsFlat==true){catalog|=work.Save().Phase>8;probe|=work.FlatNeedsGraph;relax|=work.Save().Flat.Pending;}
                    byte[] before=CaptureGraph(hot,source);var cold=new NavigationRetentionBudget();
                    Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,n+1,out var decode));
                    var copy=(LayeredNavigationScheduler)Decode(before,decode.OwnedReservation());decode.Attach(copy);decode.Commit(copy,_=>{});
                    try{
                        Assert.True(copy.IsImmutableCaptureRoot,"decoded root can be recaptured without resealing or an unsaved delegate");
                        CollectionAssert.AreEqual(before,CaptureGraph(cold,copy));
                        var quota=new NavEpochQuota(1,0,1,1,0);source=FlatPrimitive(source,hot,n,quota);copy=FlatPrimitive(copy,cold,n,quota);
                        CollectionAssert.AreEqual(CaptureGraph(hot,source),CaptureGraph(cold,copy),"same next primitive, charged pending edge and saved grants");
                    }finally{copy.ReleaseStorage();}
                    Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
                    ready=source.ReadyCount>0;
                }
                Assert.True(ready);Assert.True(catalog);Assert.True(probe);Assert.True(relax);Assert.Zero(source.Counters.GraphBuilds,"no eager adjacency graph was published");
            }finally{source.ReleaseStorage();}
            Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);Assert.Zero(hot.ActiveScopes);
        }
        [Test] public void FlatN1SameEpochCannotRefillExhaustedGraphOrConnectorGrant()
        {
            var source=NamedSchedulerSource(out var budget,out _,out _,singleton:true);
            try{
                source=FlatPrimitive(source,budget,0,new NavEpochQuota(0,0,1,0,0));Assert.True(FlatHead(source).Singleton.IsFlat);
                var bytes=CaptureGraph(budget,source);long graph=source.Counters.PhysicalGraphWork,connectors=source.Counters.ConnectorWork;
                source=FlatPrimitive(source,budget,0,new NavEpochQuota(100,100,100,100,100));
                Assert.AreEqual(graph,source.Counters.PhysicalGraphWork);Assert.AreEqual(connectors,source.Counters.ConnectorWork);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,source));
                source=FlatPrimitive(source,budget,1,new NavEpochQuota(1,0,0,0,0));Assert.AreEqual(graph+1,source.Counters.PhysicalGraphWork);
                bytes=CaptureGraph(budget,source);source=FlatPrimitive(source,budget,1,new NavEpochQuota(100,100,100,100,100));CollectionAssert.AreEqual(bytes,CaptureGraph(budget,source));
            }finally{source.ReleaseStorage();}
            Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void FlatN1LogicalCheckpointAndWrongModeRecipeFailClosed()
        {
            var root=NamedSchedulerSource(out var budget,out var first,out var second,singleton:true);
            try{
                for(int n=0;n<120;n++)root=FlatPrimitive(root,budget,n,new NavEpochQuota(1,0,1,1,0));
                var state=root.Save();var clone=LayeredNavigationScheduler.Restore(state,new[]{first,second});
                try{Assert.AreEqual(root.LogicalContinuationBinding,clone.LogicalContinuationBinding);var h=FlatHead(root);var c=FlatHead(clone);Assert.True(h.Singleton.IsFlat);Assert.AreEqual(h.Singleton.Save().Flat.Sample,c.Singleton.Save().Flat.Sample);Assert.AreEqual(h.Singleton.Save().Flat.Pending,c.Singleton.Save().Flat.Pending);}
                finally{clone.ReleaseStorage();}
                var saved=FlatHead(root).Singleton.Save();saved.Flat.RecipeKey="foreign";Assert.Throws<ArgumentException>(()=>NavIncrementalSingletonRoute.RestoreFlat(first,saved));
                var work=FlatHead(root).Singleton;var bytes=CaptureGraph(budget,work);var wrong=(byte[])bytes.Clone();
                // BODY header(8), definition marker(1), ID(4), encoded tag(4).
                Buffer.BlockCopy(BitConverter.GetBytes(new NavPhysicalTag(NavPhysicalStorageKind.Singleton).Encoded),0,wrong,13,4);
                var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));
                try{Assert.Throws<ArgumentException>(()=>Decode(wrong,scope.OwnedReservation()));}finally{scope.Discard();}
                Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            }finally{root.ReleaseStorage();}
            Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void FlatN1RefusedBirthsLeavePublishedPrimitiveAndChargesUnchanged()
        {
            var source=NamedSchedulerSource(out var budget,out _,out _,singleton:true);
            try{
                source=FlatPrimitive(source,budget,0,new NavEpochQuota(1,0,1,1,0));var bytes=CaptureGraph(budget,source);long charge=budget.PhysicalBytes;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));
                budget.AllocationAdmission=_=>false;
                try{Assert.Throws<InvalidOperationException>(()=>source.ForkOwned(scope.OwnedReservation()));}finally{budget.AllocationAdmission=null;scope.Discard();}
                Assert.AreEqual(charge,budget.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,source));
            }finally{source.ReleaseStorage();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void FlatN1PaidPendingGeometryWaitsForConnectorWithoutRefillingSameEpoch()
        {
            var root=NamedSchedulerSource(out var budget,out _,out _,singleton:true);long epoch=0;bool found=false;
            try{
                for(;epoch<6000;epoch++){
                    var w=FlatHead(root)?.Singleton;bool graph=w?.IsFlat==true&&w.FlatNeedsGraph;
                    root=FlatPrimitive(root,budget,epoch,new NavEpochQuota(graph?1:0,0,graph?0:1,1,0));
                    if(FlatHead(root)?.Singleton?.Save().Flat?.Pending==true){found=true;break;}
                }
                Assert.True(found);var bytes=CaptureGraph(budget,root);long graphWork=root.Counters.PhysicalGraphWork;
                root=FlatPrimitive(root,budget,epoch,new NavEpochQuota(100,100,100,100,100));CollectionAssert.AreEqual(bytes,CaptureGraph(budget,root));
                root=FlatPrimitive(root,budget,epoch+1,new NavEpochQuota(0,0,1,0,0));
                Assert.AreEqual(graphWork,root.Counters.PhysicalGraphWork);Assert.False(FlatHead(root).Singleton.Save().Flat.Pending);
            }finally{root.ReleaseStorage();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }
    }

    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test,Timeout(60000)] public void UnaryFlatPaidPageKeepsColdNextStepAndRefusalBeforeUnusedSuffix()
        {
            using(var fixture=new OwnedArrivalFixture(true,samePoint:true,certifiedFlat:true,flatCandidateCount:1024))
            {
                var work=fixture.NewWork();var quota=new NavEpochQuota(4,0,4,4,0);bool preparation=false,route=false,ready=false,refused=false;
                try{
                    for(int epoch=0;epoch<32&&!ready;epoch++){
                        var next=Fork(work,fixture.Budget,epoch+100,w=>w.Advance(epoch,4,quota));work.ReleaseOwner();work=next;
                        var logical=work.Save();Assert.AreEqual(24,logical.Version);Assert.Zero(logical.PreparationMember);
                        var restoredLogical=NavigationArrivalWork.Restore(fixture.Input,logical);try{Assert.AreEqual(work.LogicalContinuationBinding,restoredLogical.LogicalContinuationBinding);}finally{restoredLogical.ReleaseOwner();}
                        if(epoch==0||!route&&work.WorkPhase=="SolvePage"||work.Status==NavArrivalStatus.Ready){
                            preparation|=work.WorkPhase=="PrepareRegions";route|=ArrivalModeRequests(work).Any()||work.Proposals.Any(p=>p.Route!=null&&p.Route.Legs.Count>0);ready=work.Status==NavArrivalStatus.Ready;
                            var bytes=CaptureGraph(fixture.Budget,work);var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavigationArrivalWork restored=null,hotNext=null,coldNext=null;
                            try{
                                restored=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                                hotNext=Fork(work,fixture.Budget,10000+epoch,w=>w.Advance(epoch+1,4,quota));coldNext=Fork(restored,cold,10000+epoch,w=>w.Advance(epoch+1,4,quota));
                                CollectionAssert.AreEqual(CaptureGraph(fixture.Budget,hotNext),CaptureGraph(cold,coldNext),"Exact paid next operation includes the unary page policy");
                            }finally{hotNext?.ReleaseOwner();coldNext?.ReleaseOwner();restored?.ReleaseOwner();decode.Discard();}
                            Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
                            if(!refused){long physical=fixture.Budget.PhysicalBytes;Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Solve,1,20000+epoch,out var scope));fixture.Budget.AllocationAdmission=_=>false;
                                try{Assert.Throws<InvalidOperationException>(()=>work.ForkOwned(scope.OwnedReservation()));}finally{fixture.Budget.AllocationAdmission=null;scope.Discard();}
                                CollectionAssert.AreEqual(bytes,CaptureGraph(fixture.Budget,work));Assert.AreEqual(physical,fixture.Budget.PhysicalBytes);refused=true;}
                        }
                    }
                    Assert.True(preparation&&route&&ready&&refused,"Preparation, actual N1 route, Ready and refused fork must all be observed: prep="+preparation+" route="+route+" ready="+ready+" refused="+refused+" phase="+work.WorkPhase);
                    Assert.AreEqual(fixture.Input.Source.Candidates[0],work.Proposals.Single().Endpoint,"Earliest legal candidate is retained");
                    Assert.Less(work.CandidateWorkConsumed,fixture.Input.Source.Candidates.Count,"Unused suffix pages cannot block an already legal first-page route");
                    var invalid=work.Save().CopyLogical();invalid.QueryMode=true;Assert.Throws<ArgumentException>(()=>NavigationArrivalWork.Restore(fixture.Input,invalid));
                }finally{work.ReleaseOwner();}
            }
        }
        [Test,Timeout(60000)] public void FlatIndependentGroupKeepsOwnedModeAndNextStepAcrossBothMembers()
        {
            using(var fixture=new OwnedArrivalFixture(true,multipleCandidates:true,certifiedFlat:true,memberCount:2,
                candidateAt:(i,at)=>i==0?at:new NavLocation(new NavPoint(5,5),NavLocation.FlatSurface)))
            {
                var points=fixture.Input.Source.Candidates;var terms=fixture.Input.ArrivalProfile;double radius=fixture.Input.Members.Max(m=>m.Radius);
                double spacing=Math.Max(terms.ArrivalSlotSpacing,4*radius+2*terms.ArrivalTolerance+2*terms.LocalGridCell);
                Assert.GreaterOrEqual(LayeredNavigationProvider.Distance(points[0].Position,points[1].Position),spacing,"Both members need legal separate slots.");
                var secondMember=fixture.Input.Members[1];Assert.False(fixture.Input.WorkingFor(secondMember).Provider.CanSweep(secondMember.Origin,points[1],secondMember.Clearance),"The second member must exercise a pending detour too.");
                var work=fixture.NewWork();bool first=false,second=false,finished=false;
                var quota=new NavEpochQuota(32,0,32,32,0);
                try
                {
                    Assert.AreEqual(NavPhysicalValueKind.FlatIndependentGroupArrivalV1,((INavPhysicalBody)work).PhysicalTag.Value);
                    for(int epoch=0;epoch<4000&&!finished;epoch++)
                    {
                        var next=Fork(work,fixture.Budget,epoch+100,w=>w.Advance(epoch,32,quota));work.ReleaseOwner();work=next;
                        int member=work.Save().MemberIndex;
                        bool checkpoint=(!first&&member==0&&ArrivalModeRequests(work).Any())||(!second&&member==1)||work.Status==NavArrivalStatus.Ready;
                        if(checkpoint)
                        {
                            first|=member==0;second|=member==1;finished=work.Status==NavArrivalStatus.Ready;
                            byte[] bytes=CaptureGraph(fixture.Budget,work);
                            var wrong=(byte[])bytes.Clone();Buffer.BlockCopy(BitConverter.GetBytes(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalWork).Encoded),0,wrong,13,4);
                            var rejected=new NavigationRetentionBudget();Assert.True(rejected.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejection));
                            try{Assert.Throws<ArgumentException>(()=>Decode(wrong,rejection.OwnedReservation()),"A new group cannot be relabeled as the preceding shared mode.");}
                            finally{rejection.Discard();}
                            Assert.Zero(rejected.PhysicalBytes);Assert.Zero(rejected.HeldBytes);
                            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavigationArrivalWork restored=null,hotNext=null,coldNext=null;
                            try
                            {
                                restored=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
                                CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                                hotNext=Fork(work,fixture.Budget,10000+epoch,w=>w.Advance(epoch+1,32,quota));
                                coldNext=Fork(restored,cold,10000+epoch,w=>w.Advance(epoch+1,32,quota));
                                CollectionAssert.AreEqual(CaptureGraph(fixture.Budget,hotNext),CaptureGraph(cold,coldNext),"same paid next step in the selected group mode");
                            }
                            finally{hotNext?.ReleaseOwner();coldNext?.ReleaseOwner();restored?.ReleaseOwner();decode.Discard();}
                            Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
                        }
                    }
                    Assert.True(first&&second&&finished,$"Both member boundaries and Ready must be exercised: first={first}, second={second}, finished={finished}, status={work.Status}, proposals={work.Proposals.Count}.");
                    Assert.AreEqual(2,work.Proposals.Count);Assert.Zero(work.RouteCounters.GraphBuilds,"group requests do not materialize eager adjacency");
                    CollectionAssert.AreEqual(fixture.Input.Source.Candidates,work.Proposals.Select(x=>x.Endpoint).ToArray());
                }
                finally{work.ReleaseOwner();}
            }
        }
    }

    public sealed class NavigationFlatSingletonTests
    {
        private static NavScheduledRequest Recipe(int labels=4096,bool singleton=true,bool wall=false,double grid=1)
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(3,wall?new[]{new NavObstacle(-.25,-3,.25,3)}:new[]{new NavObstacle(-.5,-.5,.5,.5)},1));
            var from=new NavLocation(new NavPoint(-2,0),NavLocation.FlatSurface);var to=new NavLocation(new NavPoint(2,0),NavLocation.FlatSurface);
            return new NavScheduledRequest(provider,new NavClearanceProfile("ground",1,"ground",1,.1),new NavGraphProfile(new string('a',64),"distance",1,grid),new NavGraphCompileLimits(100,100,400),new NavTerminalRegion("flat",1,to,new[]{to}),new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,from,to,"held-input"),labels,singleton);
        }
        [Test] public void FlatN1CapacityCertificateIncludesSixLabelCollectionsAndUnsupportedFallback()
        {
            Assert.True(NavIncrementalSingletonRoute.FlatEligible(Recipe()));
            Assert.False(NavIncrementalSingletonRoute.FlatEligible(Recipe(labels:49)),"one distance per cell does not account for the six retained label collections");
            Assert.False(NavIncrementalSingletonRoute.FlatEligible(Recipe(singleton:false)));
            Assert.True(NavIncrementalSingletonRoute.FlatEligible(Recipe(grid:PlayableProfile.Default.Navigation.GridCell)),"The stock 0.8 grid must have a certified inverse coordinate.");
            Assert.True(NavIncrementalSingletonRoute.FlatEligible(Recipe(grid:1.5)));
            Assert.False(NavIncrementalSingletonRoute.FlatEligible(Recipe(grid:1e200)),"Uncertified numeric exponents retain the existing solver.");
            var r=Recipe(labels:49);var scheduler=new LayeredNavigationScheduler();
            try{Assert.True(scheduler.Submit(r));scheduler.Advance(0,new NavEpochQuota(0,0,1,1,0),_=>true,1);var job=scheduler.Save().Jobs.Single();Assert.AreEqual((int)LayeredNavigationScheduler.JobPhase.Graph,job.Phase);Assert.IsNull(job.Singleton);}
            finally{scheduler.ReleaseStorage();}
        }
        [Test] public void FlatN1DirectSuccessKeepsExistingPathWithoutCatalogOrGraphWork()
        {
            var r=Recipe();var direct=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,r.Subscription.Origin,new NavLocation(new NavPoint(-1,0),NavLocation.FlatSurface),"held-input");
            var request=new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,null,direct,r.MaxLabels,true);var scheduler=new LayeredNavigationScheduler();
            try{
                Assert.True(scheduler.Submit(request));for(int epoch=0;epoch<10&&scheduler.ReadyCount==0;epoch++)scheduler.Advance(epoch,new NavEpochQuota(0,0,4,4,0),_=>true);
                Assert.AreEqual(1,scheduler.ReadyCount);Assert.Zero(scheduler.Counters.PhysicalGraphWork);Assert.Zero(scheduler.Counters.GraphBuilds);
                Assert.True(scheduler.TryTake(_=>true,out var result));try{Assert.AreEqual(NavSolveStatus.Ready,result.Status);Assert.AreEqual(1,result.Route.Legs.Count);}finally{result.Release();}
            }finally{scheduler.ReleaseStorage();}
        }
        [TestCase(false,1d)][TestCase(true,1d)][TestCase(false,.8d)][TestCase(true,.8d)][TestCase(false,1.5d)] public void FlatN1ImplicitTopologyKeepsCanonicalN1StatusCostAndTieRoute(bool wall,double grid)
        {
            var r=Recipe(wall:wall,grid:grid);var compiler=new LayeredNavigationCompileWork(r.Provider,r.Clearance,r.Profile,r.Limits);
            while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready)compiler.Advance(16);
            var eager=new NavIncrementalSingletonRoute(compiler.Graph,r.Subscription,r.MaxLabels);var lazy=NavIncrementalSingletonRoute.CreateFlat(r);
            try{
                int operations=0;while(eager.Status==NavSolveStatus.Pending){Assert.Less(++operations,20000);eager.Advance(1);}
                operations=0;while(lazy.Status==NavSolveStatus.Pending){Assert.Less(++operations,20000);lazy.StepFlat(lazy.FlatNeedsGraph);}
                Assert.AreEqual(eager.Status,lazy.Status);
                if(eager.Route!=null){Assert.AreEqual(eager.Route.Cost,lazy.Route.Cost);Assert.AreEqual(eager.Route.Legs.Count,lazy.Route.Legs.Count);for(int n=0;n<eager.Route.Legs.Count;n++){Assert.AreEqual(eager.Route.Legs[n].From,lazy.Route.Legs[n].From);Assert.AreEqual(eager.Route.Legs[n].To,lazy.Route.Legs[n].To);Assert.AreEqual(eager.Route.Legs[n].Cost,lazy.Route.Legs[n].Cost);}}
            }finally{eager.ReleaseOwner();lazy.ReleaseOwner();compiler.ReleaseOwner();}
        }
        [Test] public void StockFlatCandidateSelectsLazyModeAndReportsPreparationWork()
        {
            var p=PlayableProfile.Default;var from=new NavLocation(new NavPoint(-20,-27),NavLocation.FlatSurface);var to=new NavLocation(new NavPoint(16.4,23.4),NavLocation.FlatSurface);
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1));
            var r=new NavScheduledRequest(provider,new NavClearanceProfile("ground",1,"ground",1,PlayableUnitRules.Radius(p,PlayableEntityKind.Explorer)),
                new NavGraphProfile(new string('e',64),"distance",1,p.Navigation.GridCell),new NavGraphCompileLimits(1048576,300000,1200000),null,
                new NavRouteSubscription(107,47,1,1,0,1,9,1,1,1,117,from,to,null),1048576,singleton:true);
            var scheduler=new LayeredNavigationScheduler();
            try
            {
                Assert.True(NavIncrementalSingletonRoute.FlatEligible(r));Assert.True(scheduler.Submit(r));
                scheduler.Advance(0,new NavEpochQuota(0,0,1,0,0),_=>true,1);var selected=scheduler.Save().Jobs.Single();Assert.NotNull(selected.Singleton);Assert.AreEqual(4,selected.Singleton.Version,"The actual stock grid selects the explicit flat checkpoint after blocked Direct.");Assert.NotNull(selected.Singleton.Flat);Assert.IsNull(selected.Compiler);Assert.IsNull(selected.Graph);
                int epoch=1;while(scheduler.ReadyCount==0&&epoch<4000){scheduler.Advance(epoch,new NavEpochQuota(256,0,256,256,256),_=>true);epoch++;}
                Assert.True(scheduler.TryTake(_=>true,out var result));try{Assert.AreEqual(NavSolveStatus.Ready,result.Status);}finally{result.Release();}
                Assert.Zero(scheduler.Counters.GraphBuilds);TestContext.WriteLine("STOCK_FLAT_MAP_ONLY "+Newtonsoft.Json.JsonConvert.SerializeObject(new{epochs=epoch,grid=p.Navigation.GridCell,scheduler.Counters.PhysicalGraphWork,scheduler.Counters.ConnectorWork}));
            }
            finally{scheduler.ReleaseStorage();}
        }
        private static void SameFlatState(NavIncrementalSingletonRoute.Checkpoint a,NavIncrementalSingletonRoute.Checkpoint b)
        {
            Assert.AreEqual(a.Version,b.Version);Assert.AreEqual(a.GraphKey,b.GraphKey);Assert.AreEqual(a.SubscriptionKey,b.SubscriptionKey);Assert.AreEqual(a.Status,b.Status);
            CollectionAssert.AreEqual(new[]{a.Phase,a.Cursor,a.ArcCursor,a.Current,a.Chosen,a.ReconstructCursor,a.ReconstructLeg,a.TraceNode,a.Flat.Sample},new[]{b.Phase,b.Cursor,b.ArcCursor,b.Current,b.Chosen,b.ReconstructCursor,b.ReconstructLeg,b.TraceNode,b.Flat.Sample});
            Assert.AreEqual(a.Best,b.Best);Assert.AreEqual(a.RouteCost,b.RouteCost);Assert.AreEqual(a.StartSeeded,b.StartSeeded);Assert.AreEqual(a.Flat.Pending,b.Flat.Pending);Assert.AreEqual(a.Flat.HasEdge,b.Flat.HasEdge);Assert.AreEqual(a.Flat.Edge,b.Flat.Edge);
            CollectionAssert.AreEqual(a.Flat.Nodes,b.Flat.Nodes);CollectionAssert.AreEqual(a.Starts,b.Starts);CollectionAssert.AreEqual(a.Ends,b.Ends);CollectionAssert.AreEqual(a.Distance,b.Distance);CollectionAssert.AreEqual(a.Previous,b.Previous);CollectionAssert.AreEqual(a.Frontier,b.Frontier);CollectionAssert.AreEqual(a.Chain,b.Chain);
            Assert.AreEqual(a.Legs.Length,b.Legs.Length);for(int n=0;n<a.Legs.Length;n++){Assert.AreEqual(a.Legs[n].From,b.Legs[n].From);Assert.AreEqual(a.Legs[n].To,b.Legs[n].To);Assert.AreEqual(a.Legs[n].Cost,b.Legs[n].Cost);Assert.AreEqual(a.Legs[n].Kind,b.Legs[n].Kind);}
        }
        [TestCase(1d)][TestCase(.8d)] public void FlatN1LogicalRestoreContinuesEveryPrimitiveAndRejectsAlteredPendingProof(double grid)
        {
            var basis=Recipe(grid:grid);
            // The original center box admits a common visible attachment node,
            // so a correct two-leg route has no predecessor chain. These two
            // staggered supports require a real grid walk between disjoint
            // visible-origin and visible-endpoint sets, inside the SAME bounds.
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(3,new[]{new NavObstacle(-.75,-3,-.25,.25),new NavObstacle(.25,-.25,.75,3)},1));
            var recipe=new NavScheduledRequest(provider,basis.Clearance,basis.Profile,basis.Limits,basis.Region,basis.Subscription,basis.MaxLabels,true);
            var source=NavIncrementalSingletonRoute.CreateFlat(recipe);bool pending=false,malformed=false,trace=false;int operations=0;
            try{
                while(source.Status==NavSolveStatus.Pending){
                    Assert.Less(++operations,20000);var before=source.Save();var restored=NavIncrementalSingletonRoute.RestoreFlat(recipe,before);
                    try{
                        SameFlatState(before,restored.Save());bool graph=source.FlatNeedsGraph;Assert.AreEqual(graph,restored.FlatNeedsGraph);source.StepFlat(graph);restored.StepFlat(graph);SameFlatState(source.Save(),restored.Save());
                    }finally{restored.ReleaseOwner();}
                    pending|=before.Flat.Pending;trace|=before.Chain.Length>0;
                    if(!malformed&&before.Flat.Pending&&before.Flat.HasEdge){
                        before.ArcCursor=(before.ArcCursor+1)%4;
                        Assert.Throws<ArgumentException>(()=>NavIncrementalSingletonRoute.RestoreFlat(recipe,before),"a paid proof cannot be rebound to another neighbor cursor");malformed=true;
                    }
                }
                TestContext.WriteLine("FLAT_LOGICAL_COVERAGE pending="+pending+" malformed="+malformed+" trace="+trace+" primitives="+operations);
                Assert.AreEqual(NavSolveStatus.Ready,source.Status);Assert.True(pending,"paid geometry boundary reached");Assert.True(malformed,"altered pending-neighbor proof was rejected");Assert.True(trace,"nonempty predecessor chain was resumed");
            }finally{source.ReleaseOwner();}
        }
        [Test] public void FlatIndependentGroupKeepsEarliestCandidateAndLogicalMode()
        {
            var r=Recipe();var earlier=r.Subscription.AssignedEndpoint;var later=new NavLocation(new NavPoint(-2,2),NavLocation.FlatSurface);
            var members=new[]{new NavArrivalMember(1,1,1,1,r.Subscription.Origin,r.Clearance,1,1,1,1,0,1,1,1),
                new NavArrivalMember(2,1,1,1,new NavLocation(new NavPoint(2,-2),NavLocation.FlatSurface),r.Clearance,1,1,1,1,0,1,1,2)};
            var input=new NavArrivalInput("flat-group",new string('b',64),r.Provider,r.Profile,new NavigationProfile(),r.Limits,
                new NavArrivalCandidateSource("flat-group",1,earlier,new[]{earlier,later}),members,new NavArrivalOccupancySnapshot(1,Array.Empty<NavArrivalOccupant>()),r.MaxLabels,independentMembers:true);
            var work=new NavigationArrivalWork(input);bool pending=false,second=false;
            try
            {
                for(int epoch=0;epoch<10000&&(work.Status==NavArrivalStatus.Pending||work.Status==NavArrivalStatus.BudgetExhausted);epoch++)
                {
                    work.Advance(epoch,16,new NavEpochQuota(16,0,16,16,0));var saved=work.Save();Assert.AreEqual(24,saved.Version);
                    if(saved.Requests.Length>0){Assert.True(saved.Requests.All(x=>x.Singleton));
                        if(!pending){var mixed=saved.CopyLogical();mixed.QueryMode=true;Assert.Throws<ArgumentException>(()=>NavigationArrivalWork.Restore(input,mixed),"Flat mode cannot import the Partition query policy.");}
                        pending=true;}
                    second|=saved.MemberIndex==1;
                    var restored=NavigationArrivalWork.Restore(input,saved);
                    try{Assert.AreEqual(work.LogicalContinuationBinding,restored.LogicalContinuationBinding);}
                    finally{restored.ReleaseOwner();}
                }
                Assert.True(pending&&second);Assert.AreEqual(NavArrivalStatus.Ready,work.Status);
                CollectionAssert.AreEqual(new[]{earlier,later},work.Proposals.Select(x=>x.Endpoint).ToArray(),"Later Direct success must not bypass the first member's earlier reachable candidate, and occupancy excludes it for the second member.");
            }
            finally{work.ReleaseOwner();}
        }
        [Test] public void ExistingFlatGroupCheckpointRetainsItsSharedPolicy()
        {
            var r=Recipe();var members=new[]{new NavArrivalMember(1,1,1,1,r.Subscription.Origin,r.Clearance,1,1,1,1,0,1,1,1),new NavArrivalMember(2,1,1,1,r.Subscription.Origin,r.Clearance,1,1,1,1,0,1,1,2)};
            var input=new NavArrivalInput("flat-current-shared",new string('b',64),r.Provider,r.Profile,new NavigationProfile(),r.Limits,new NavArrivalCandidateSource("flat-current-shared",1,r.Subscription.AssignedEndpoint,new[]{r.Subscription.AssignedEndpoint}),members,new NavArrivalOccupancySnapshot(1,Array.Empty<NavArrivalOccupant>()),r.MaxLabels,independentMembers:true);
            var old=NavigationArrivalWork.RestoreLegacyJournal(input);
            try
            {
                for(int epoch=0;epoch<20&&old.Save().Requests.Length==0;epoch++)old.Advance(epoch,100,new NavEpochQuota(0,0,0,0,0));
                var saved=old.Save();Assert.AreEqual(2,saved.Version);Assert.True(saved.Requests.Length>0);Assert.False(saved.Requests.Single().Singleton);
                var restored=NavigationArrivalWork.Restore(input,saved);
                try{Assert.AreEqual(old.LogicalContinuationBinding,restored.LogicalContinuationBinding);Assert.AreEqual(2,restored.Save().Version);Assert.False(restored.Save().Requests.Single().Singleton);}
                finally{restored.ReleaseOwner();}
            }
            finally{old.ReleaseOwner();}
        }
        [Test] public void FlatN1ArrivalRetainsEarlierReachableCandidateBeforeLaterDirectCandidate()
        {
            var r=Recipe();var earlier=r.Subscription.AssignedEndpoint;var later=new NavLocation(new NavPoint(-2,2),NavLocation.FlatSurface);
            var member=new NavArrivalMember(1,1,1,1,r.Subscription.Origin,r.Clearance,1,1,1,1,0,1,1,1);
            var input=new NavArrivalInput("flat-order",new string('b',64),r.Provider,r.Profile,new NavigationProfile(),r.Limits,new NavArrivalCandidateSource("flat-order",1,earlier,new[]{earlier,later}),new[]{member},new NavArrivalOccupancySnapshot(1,Array.Empty<NavArrivalOccupant>()),r.MaxLabels,independentMembers:true);
            var work=new NavigationArrivalWork(input);
            try{for(int epoch=0;epoch<10000&&(work.Status==NavArrivalStatus.Pending||work.Status==NavArrivalStatus.BudgetExhausted);epoch++)work.Advance(epoch,16,new NavEpochQuota(16,0,16,16,0));
                Assert.AreEqual(NavArrivalStatus.Ready,work.Status);Assert.AreEqual(earlier,work.Proposals.Single().Endpoint,"later direct success cannot bypass an earlier reachable detour");}
            finally{work.ReleaseOwner();}
        }
    }
}

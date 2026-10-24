using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationPartitionQueryTests
    {
        private static NavScheduledRequest Request(int labels=4096)
        {
            var map=ThreeCrossingsMap.Default;
            var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);
            var profile=new NavGraphProfile(new string('b',64),"distance",1,16);
            var limits=new NavGraphCompileLimits(2000,2000,10000);
            var graph=LayeredNavigationGraphCompiler.Compile(provider,clearance,profile,limits);
            var origin=graph.Nodes[0].Location;var end=graph.Nodes[graph.Nodes.Count-1].Location;
            var other=new[]{new NavPoint(-.125,0),new NavPoint(.125,0),new NavPoint(0,-.125),new NavPoint(0,.125)}.Select(d=>new NavLocation(new NavPoint(end.Position.X+d.X,end.Position.Z+d.Z),end.SurfaceId)).First(at=>graph.IsValid(at)&&graph.CanSweep(at,end));
            var region=new NavTerminalRegion("query-test",1,end,new[]{end,other});
            var sub=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,origin,end,null);
            graph.ReleaseOwner();
            return new NavScheduledRequest(provider,clearance,profile,limits,region,sub,labels,query:true);
        }
        [Test] public void QueryLogicalCheckpointPreservesPhasePriorityAndExactNextWork()
        {
            var request=Request();var graph=LayeredNavigationGraphCompiler.Compile(request.Provider,request.Clearance,request.Profile,request.Limits);
            var work=new NavIncrementalSingletonRoute(graph,request.Subscription,request.MaxLabels,request.Region);
            var phases=new System.Collections.Generic.HashSet<int>();
            try{
                for(int step=0;work.Status==NavSolveStatus.Pending;step++){
                    Assert.Less(step,20000);var saved=work.Save();Assert.AreEqual(3,saved.Version);phases.Add(saved.Phase);
                    var restored=NavIncrementalSingletonRoute.Restore(graph,request.Subscription,saved,request.MaxLabels,request.Region);
                    try{
                        Assert.AreEqual(work.Advance(1).Consumed,restored.Advance(1).Consumed);
                        var a=work.Save();var b=restored.Save();
                        Assert.AreEqual(a.Phase,b.Phase);Assert.AreEqual(a.Cursor,b.Cursor);Assert.AreEqual(a.Current,b.Current);Assert.AreEqual(a.ArcCursor,b.ArcCursor);
                        Assert.AreEqual(a.QueryCandidate,b.QueryCandidate);Assert.AreEqual(a.QueryChosenCandidate,b.QueryChosenCandidate);Assert.AreEqual(a.Chosen,b.Chosen);
                        Assert.AreEqual(a.Best,b.Best);Assert.AreEqual(a.RouteCost,b.RouteCost);Assert.AreEqual(a.ReconstructCursor,b.ReconstructCursor);Assert.AreEqual(a.ReconstructLeg,b.ReconstructLeg);
                        CollectionAssert.AreEqual(a.Starts,b.Starts);CollectionAssert.AreEqual(a.Ends,b.Ends);CollectionAssert.AreEqual(a.Distance,b.Distance);CollectionAssert.AreEqual(a.Frontier,b.Frontier);
                        Assert.AreEqual(a.Status,b.Status);Assert.AreEqual(a.Legs.Length,b.Legs.Length);
                        for(int i=0;i<a.Legs.Length;i++){Assert.AreEqual(a.Legs[i].From,b.Legs[i].From);Assert.AreEqual(a.Legs[i].To,b.Legs[i].To);Assert.AreEqual(a.Legs[i].Cost,b.Legs[i].Cost);Assert.AreEqual(a.Legs[i].Kind,b.Legs[i].Kind);Assert.AreEqual(a.Legs[i].PortalId,b.Legs[i].PortalId);}
                    }finally{restored.ReleaseOwner();}
                }
                Assert.AreEqual(NavSolveStatus.Ready,work.Status);Assert.Greater(phases.Count,5);
                Assert.AreEqual(request.Subscription.Origin,work.Route.Legs[0].From);Assert.AreEqual(request.Subscription.AssignedEndpoint,work.Route.Legs.Last().To);
                // Seam trace surface sublegs are certified in the context of the
                // complete typed arc; re-running a whole-connector sweep on one
                // side of a zero-length surface change has different selectors.
                var complete=work.Save();foreach(var arc in complete.Chain){var exact=graph.ResolveCheckpointArc(arc);Assert.AreEqual(arc.Cost,exact.Cost);Assert.AreEqual(arc.Kind,exact.Kind);Assert.AreEqual(arc.Trace?.Count,exact.Trace?.Count);}
                int first=complete.Chain.Length==0?complete.Chosen:complete.Chain.Last().From;
                Assert.True(graph.CanSweep(request.Subscription.Origin,graph.Nodes[first].Location));
                Assert.True(graph.CanSweep(graph.Nodes[complete.Chosen].Location,request.Region.Candidates[complete.QueryChosenCandidate]));
                Assert.True(graph.CanSweep(request.Region.Candidates[complete.QueryChosenCandidate],request.Subscription.AssignedEndpoint));
            }finally{work.ReleaseOwner();graph.ReleaseOwner();}
        }
        [Test] public void QueryCapGuardFallsBackWithoutRefillingQuotaOrChangingLegacyRecipeKey()
        {
            var request=Request(1);var old=new NavScheduledRequest(request.Provider,request.Clearance,request.Profile,request.Limits,request.Region,request.Subscription,1);
            Assert.AreNotEqual(old.ArtifactKey,request.ArtifactKey);Assert.AreEqual(old.CompileKey,request.CompileKey);
            var scheduler=new LayeredNavigationScheduler();Assert.True(scheduler.Submit(request));
            try{
                int epoch=0;for(;epoch<20000&&scheduler.ReadyCount==0;epoch++){
                    scheduler.Advance(epoch,new NavEpochQuota(1,1,1,1,1),_=>true);
                    Assert.Zero(scheduler.Advance(epoch,new NavEpochQuota(1000,1000,1000,1000,1000),_=>true),"same epoch cannot refill any lane");
                }
                Assert.Less(epoch,20000);Assert.Greater(scheduler.Counters.PhysicalFieldWork,0,"uncertified N*C capacity uses legacy shared field");
                Assert.True(scheduler.TryTake(_=>true,out var answer));try{Assert.AreEqual(NavSolveStatus.CapacityExceeded,answer.Status);}finally{answer.ReleaseOwner();}
            }finally{scheduler.ClearWorld();scheduler.ReleaseOwner();}
        }
        [Test] public void QueryCheckpointRejectsCandidatePhaseCursorBeforeAdvance()
        {
            var request=Request();var graph=LayeredNavigationGraphCompiler.Compile(request.Provider,request.Clearance,request.Profile,request.Limits);
            var work=new NavIncrementalSingletonRoute(graph,request.Subscription,request.MaxLabels,request.Region);
            try{
                work.Advance(1);var saved=work.Save();saved.Cursor=request.Region.Candidates.Count+1;
                Assert.Throws<ArgumentException>(()=>NavIncrementalSingletonRoute.Restore(graph,request.Subscription,saved,request.MaxLabels,request.Region));
            }finally{work.ReleaseOwner();graph.ReleaseOwner();}
        }
        [Test] public void LegacyArrivalJournalKeepsOldPolicyAndTwoRequestsWhileNewQueryIsSerial()
        {
            var request=Request();var sub=request.Subscription;
            var member=new NavArrivalMember(sub.EntityId,sub.Incarnation,sub.MemberOrderRevision,sub.MobilityRevision,sub.Origin,request.Clearance,sub.WorldGeneration,sub.GroupId,sub.RootOrderRevision,sub.CommandSequence,sub.IssuedTick,sub.ActivationRevision,sub.MemberOrderRevision,sub.RequestSequence);
            var source=new NavArrivalCandidateSource("legacy-arrival",1,request.Region.Requested,request.Region.Candidates);
            var input=new NavArrivalInput("topology:"+request.Provider.Binding,"profile:"+request.Profile.ExactProfileBinding,request.Provider,request.Profile,new NavigationProfile(),request.Limits,source,new[]{member},NavigationArrivalTestFactory.Occupancy(),request.MaxLabels,null,true);
            var legacy=NavigationArrivalWork.RestoreLegacyJournal(input);var current=new NavigationArrivalWork(input);NavigationArrivalWork restored=null;
            try{
                var zero=new NavEpochQuota(0,0,0,0,0);
                Assert.AreEqual(NavArrivalStatus.BudgetExhausted,legacy.Advance(0,10000,zero));Assert.AreEqual(2,legacy.PendingRouteCount);
                Assert.AreEqual(NavArrivalStatus.BudgetExhausted,current.Advance(0,10000,zero));Assert.AreEqual(1,current.PendingRouteCount);
                Assert.AreEqual(2,legacy.Save().Version);Assert.False(legacy.Save().QueryMode);Assert.True(legacy.Save().Requests.All(r=>!r.Query));
                Assert.AreEqual(5,current.Save().Version);Assert.True(current.Save().DemandQueryMode);Assert.True(current.Save().QueryMode);Assert.True(current.Save().Requests.All(r=>r.Query));
                restored=NavigationArrivalWork.Restore(input,legacy.Save());Assert.False(restored.Save().QueryMode);
                var quota=new NavEpochQuota(1,1,1,1,1);Assert.AreEqual(legacy.Advance(1,1,quota),restored.Advance(1,1,quota));Assert.AreEqual(legacy.RouteCounters.LogicalGraphWork,restored.RouteCounters.LogicalGraphWork);Assert.AreEqual(legacy.PendingRouteCount,restored.PendingRouteCount);
            }finally{legacy.ReleaseOwner();current.ReleaseOwner();restored?.ReleaseOwner();}
        }
        [Test] public void GroupQueryPolicyVersionPreservesLegacyMultiMemberCheckpoint()
        {
            var request=Request();var graph=LayeredNavigationGraphCompiler.Compile(request.Provider,request.Clearance,request.Profile,request.Limits);
            var members=Enumerable.Range(0,3).Select(i=>NavigationArrivalTestFactory.Member(100+i,0,graph.Nodes[i].Location,.25,9)).ToArray();
            var input=new NavArrivalInput("topology:"+request.Provider.Binding,"profile:"+request.Profile.ExactProfileBinding,request.Provider,request.Profile,new NavigationProfile(),request.Limits,new NavArrivalCandidateSource("group-policy",1,request.Region.Requested,request.Region.Candidates),members,NavigationArrivalTestFactory.Occupancy(),request.MaxLabels,null,true);
            var legacy=NavigationArrivalWork.RestoreLegacyJournal(input);var current=new NavigationArrivalWork(input);NavigationArrivalWork restored=null;
            try{
                var zero=new NavEpochQuota(0,0,0,0,0);legacy.Advance(0,10000,zero);current.Advance(0,10000,zero);
                Assert.AreEqual(2,legacy.Save().Version);Assert.False(legacy.Save().QueryMode);Assert.False(legacy.Save().GroupQueryMode);Assert.AreEqual(2,legacy.PendingRouteCount);Assert.True(legacy.Save().Requests.All(r=>!r.Query&&!r.GroupQuery));
                Assert.AreEqual(5,current.Save().Version);Assert.True(current.Save().DemandQueryMode);Assert.True(current.Save().QueryMode);Assert.True(current.Save().GroupQueryMode);Assert.AreEqual(1,current.PendingRouteCount);Assert.True(current.Save().Requests.All(r=>r.Query&&r.GroupQuery));
                restored=NavigationArrivalWork.Restore(input,legacy.Save());Assert.False(restored.Save().GroupQueryMode);Assert.False(restored.Save().QueryMode);
                var one=new NavEpochQuota(1,1,1,1,1);Assert.AreEqual(legacy.Advance(1,1,one),restored.Advance(1,1,one));Assert.AreEqual(legacy.RouteCounters.LogicalGraphWork,restored.RouteCounters.LogicalGraphWork);
                restored.ReleaseOwner();restored=NavigationArrivalWork.Restore(input,current.Save());Assert.True(restored.Save().GroupQueryMode);Assert.True(restored.Save().Requests.All(r=>r.GroupQuery));
            }finally{legacy.ReleaseOwner();current.ReleaseOwner();restored?.ReleaseOwner();graph.ReleaseOwner();}
        }
        [TestCase(false)][TestCase(true)] public void ExplicitGroupCachePolicyDoesNotChangeOldSingletonNextEndpoint(bool group)
        {
            var r=Request();var s=r.Subscription;
            var first=new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,s,r.MaxLabels,query:true,groupQuery:group);
            var nextSub=new NavRouteSubscription(s.WorldGeneration,s.GroupId,s.RootOrderRevision,s.CommandSequence,s.IssuedTick,s.ActivationRevision,s.EntityId,s.Incarnation,s.MemberOrderRevision,s.MobilityRevision,s.RequestSequence+1,s.Origin,r.Region.Candidates[1],s.HoldBinding);
            var next=new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,nextSub,r.MaxLabels,query:true,groupQuery:group);
            var scheduler=new LayeredNavigationScheduler();scheduler.BindAttemptArtifacts(NavigationArrivalTestFactory.Sha("query-policy"));
            try{
                int epoch=0;foreach(var request in new[]{first,next}){
                    Assert.True(scheduler.Submit(request));for(int i=0;scheduler.ReadyCount==0;i++){Assert.Less(i,20000);scheduler.Advance(epoch++,new NavEpochQuota(10000,10000,10000,10000,10000),_=>true);}
                    Assert.True(scheduler.TryTake(_=>true,out var answer));try{Assert.AreEqual(NavSolveStatus.Ready,answer.Status);}finally{answer.ReleaseOwner();}
                }
                Assert.AreEqual(group?1:2,scheduler.Counters.GraphBuilds,"old query-v1 retains recompile policy; explicit group policy borrows its certified graph");
            }finally{scheduler.ClearWorld();scheduler.ReleaseOwner();}
        }
        [Test,Timeout(180000)] public void ActualQueryHostColdBytesPreserveRecipeArrivalAndNextPump()
        {
            var map=new ThreeCrossingsMap(ScoutWitnessEnvironment.Load<ThreeCrossingsProfileData>("ThreeCrossingsProfile"));
            var profile=PlayableProfile.Create(ScoutWitnessEnvironment.Load<PlayableProfileData>("PlayableProfile"),map);
            const int seed=-515260688;
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=seed};setup.InitializeParticipants();
            for(int i=0;i<setup.Participants.Count;i++){var p=setup.Participants[i];p.Human=false;p.Team=i+1;p.Name="ИИ "+(i+1);p.Difficulty=i==0?AiDifficulty.Fighter:AiDifficulty.Veteran;}
            Assert.AreEqual(2,setup.Participants.Count);Assert.True(setup.Spectator);Assert.IsNull(setup.Validate(profile,true,true));
            var configuration=NativeLobbyMatch.Create(profile,setup);
            var domain=new PlayableDomain(profile,1,configuration);NavigationSession session=domain.Navigation;var geometry=domain.Geometry;var kernel=new CertifiedRouteKernel();
            var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),seed,configuration,asynchronousRoutes:true);
            byte[] Bytes(){using(var state=domain.CaptureWorldState(seed,"query-cold"))return state.Encode();}
            bool QuerySearch(){
                var root=session.PinCommittedHostRoot();if(root==null)return false;
                try{foreach(var pair in root.ArrivalWork){
                    var work=(NavigationArrivalWork)pair.Value;var scheduler=typeof(NavigationArrivalWork).GetField("scheduler",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(work);
                    var jobs=(System.Collections.IEnumerable)scheduler.GetType().GetField("jobs",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(scheduler);
                    foreach(var job in jobs){var t=job.GetType();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var recipe=(NavScheduledRequest)t.GetProperty("Request",flags).GetValue(job);var solver=t.GetProperty("Demand",flags).GetValue(job)??t.GetProperty("Singleton",flags).GetValue(job);
                        if(solver!=null){Assert.True(recipe.Query);Assert.Greater(recipe.Region.Candidates.Count,1);var phase=solver.GetType().GetField("phase",flags).GetValue(solver).ToString();if(phase=="Search"||phase=="QueryCandidates"||phase=="Seed"||phase=="Pop"||phase=="Candidates"||phase=="Edges")return true;}
                    }
                }return false;}finally{root.Release();}
            }
            try{
                int id=3;Assert.True(session.Crowd.TryGet(id,out var scout));Assert.AreEqual(new NavPoint(-36,-46),scout.Position,"use the existing ordinary starter scout; do not spawn over it");
                var receipt=authority.Apply(new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-37.58,0),origin:PlayableOrderOrigin.Human));Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                for(int i=0;!QuerySearch();i++){
                    Assert.Less(i,20000);kernel.Pump(new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort),4);Assert.True(authority.TryAdvance(),"ordinary async authority owns issue/consume of each quantum");
                }
                byte[] prefix=Bytes();kernel.Pump(new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort),1);byte[] next=Bytes();
                var oldBudget=session.PhysicalHostBudget;kernel.Clear();authority.Stop();session.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(oldBudget.PhysicalBytes);Assert.Zero(oldBudget.HeldBytes);Assert.Zero(oldBudget.ActiveScopes);
                domain=PlayableDomain.RestoreConfiguredWorldBytes(prefix,profile,seed,"query-cold",configuration);session=domain.Navigation;geometry=domain.Geometry;kernel=new CertifiedRouteKernel();authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),seed,configuration,asynchronousRoutes:true);
                CollectionAssert.AreEqual(prefix,Bytes());kernel.Pump(new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort),1);CollectionAssert.AreEqual(next,Bytes(),"new recipe/arrival/solver BODY and exact next Pump");
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{kernel.Clear();authority.Stop();session.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
        }
    }
}

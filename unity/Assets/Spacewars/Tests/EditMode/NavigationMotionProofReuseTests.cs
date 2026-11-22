using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationMotionProofReuseTests
    {
        static NavigationSession Ready(bool independent=false,bool third=false,bool crimson=false,IPlayableTerrain terrain=null)
        {
            var p=terrain!=null?PlayableProfile.Create(PlayableProfile.RecastGroupCorridorDefault.CopyData(),terrain):
                crimson?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CrimsonGroveMap()):PlayableProfile.RecastGroupCorridorDefault;
            var map=p.AuthoredMap;
            var g=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var s=new NavigationSession(801,g,p.Navigation,null,map){UsesRecastGroundRoutes=true};
            s.ConfigureMarch(4,p.Revision);
            var grove=map as CrimsonGroveMap;
            var starts=map is AntennaCityMap city?new[]{city.RampFoot(0,1,4),city.RampFoot(0,1,7)}:
                map is MossHighlandMap?new[]{new NavPoint(16,0),new NavPoint(19,0)}:
                crimson?new[]{grove.Polar(22.5,116),grove.Polar(24,116)}:new[]{new NavPoint(-27.25,42),new NavPoint(-27.25,48)};
            s.Crowd.Add(1,starts[0],.72,4,4.5,canYield:true);
            s.Crowd.Add(2,starts[1],.72,4,4.5,canYield:true);
            if(third)s.Crowd.Add(3,new NavPoint(-27.25,54),.72,4,4.5,canYield:true);
            if(independent){
                Assert.True(s.MoveGroup(new[]{1},new[]{new NavPoint(-46,46)}));
                Assert.True(s.MoveGroup(new[]{2},new[]{new NavPoint(-42.4,42.4)}));
                if(third)Assert.True(s.MoveGroup(new[]{3},new[]{new NavPoint(-42.4,53)}));
            }else{
                var goals=map is AntennaCityMap cityMap?new[]{cityMap.RampTop(0,1,6),cityMap.RampTop(0,1,10)}:
                    map is MossHighlandMap?new[]{new NavPoint(8,0),new NavPoint(4,0)}:
                    crimson?new[]{grove.Polar(202.5,116),grove.Polar(204,116)}:new[]{new NavPoint(-46,46),new NavPoint(-42.4,42.4)};
                Assert.True(s.MoveGroup(new[]{1,2},goals));
            }
            int routes=third?3:2;
            using(var host=new UnityHostRouteService()){var binding=new PlayableRouteBinding(g,p,s.Admission,s.RoutePort);for(int i=0;i<200&&s.AppliedResults<routes;i++){host.Service(binding,8);s.ApplyResults();}}
            Assert.AreEqual(routes,s.AppliedResults);return s;
        }
        static NavPreparedCrowdStep Frame(NavigationSession s,INavStorageReservation r,bool move=false,bool local=false)
        {
            var f=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,r);try{f.Observe();
            for(int i=0;i<f.After.Length;i++){
                var a=f.After[i];var route=((NavigationInstalledExecutionOwner)f.Source[i].StorageOwner.InstalledExecution).Corridor.TypedRoute;
                var state=NavRecastIntervals.Initial(route);f.BindRecast(i,route,state);
                var from=a.Location;
                if(move){
                    var target=a.route.Skip(a.routeIndex).First(t=>!t.Equals(a.Position));double dx=target.X-a.Position.X,dz=target.Z-a.Position.Z,length=Math.Sqrt(dx*dx+dz*dz);
                    var point=new NavPoint(a.Position.X+dx/length*.02,a.Position.Z+dz/length*.02);
                    Assert.True(NavRecastIntervals.Advance(route,state,from,point,r,out var receipt,f.Working,a.Id));
                    Assert.True(NavRecastIntervals.TryTraceOwned(route,state,from,point,r,out var at,out _,out _,out var trace,f.Working,a.Id));
                    try{a.Position=point;a.Location=at;}finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
                }
                if(local)a.localRoute=new[]{a.Position}; // complete fresh local prefix, not reused
                Assert.True(NavRecastIntervals.Advance(route,state,from,a.Position,r,out f.RecastReceipts[i],f.Working,a.Id));
            }
            return f;}catch{f.Release();throw;}
        }
        static void Same(NavMotionTranscript fresh,NavMotionTranscript reused)
        {
            Assert.True(AiCombatMicroMotionAuthority.SameTranscript(fresh,fresh));
            Assert.True(AiCombatMicroMotionAuthority.SameTranscript(fresh,reused),"Distinct equal transcripts still use the full value match");
            Assert.AreEqual(fresh.Count,reused.Count);
            for(int i=0;i<fresh.Count;i++){
                var a=fresh[i];var b=reused[i];Assert.AreEqual(a.Continuation,b.Continuation);Assert.AreEqual(a.After.Location,b.After.Location);
                Assert.AreEqual(a.Chord.Count,b.Chord.Count);Assert.AreEqual(a.Remaining.Count,b.Remaining.Count);
                foreach(var pair in a.Chord.Concat(a.Remaining).Zip(b.Chord.Concat(b.Remaining),(x,y)=>(x,y))){Assert.AreEqual(pair.x.Kind,pair.y.Kind);Assert.AreEqual(pair.x.From,pair.y.From);Assert.AreEqual(pair.x.To,pair.y.To);Assert.AreEqual(pair.x.PortalId,pair.y.PortalId);Assert.AreEqual(pair.x.Cost,pair.y.Cost);}
            }
        }
        [TestCase("Crimson")]
        [TestCase("Moss")]
        public void SharedDetourFallbackOnAuthoredMapUsesOnePreparedPrefix(string map)
        {
            var session=Ready(terrain:map=="Moss"?new MossHighlandMap():null,crimson:map=="Crimson");
            var budget=session.PhysicalHostBudget;
            try
            {
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope)
                {
                    var reservation=scope.OwnedReservation();
                    var frame=Frame(session,reservation);
                    try
                    {
                        int row=frame.Find(2);
                        var actor=frame.After[row];
                        var blocker=frame.After[frame.Find(1)];
                        var route=frame.RecastRoutes[row];
                        var state=frame.RecastStates[row];
                        var goal=actor.GoalLocation.Value;
                        Assert.Greater(actor.route.Length,3);
                        blocker.Position=goal.Position;
                        blocker.Location=goal;
                        blocker.Held=true;
                        Assert.True(NavRecastIntervals.TryPrepareContinuationPrefix(route,state,actor.Location,
                            Array.Empty<NavPoint>(),0,reservation,frame.Working,actor.Id,out var at,out var after));
                        var originalMemo=default(NavRecastIntervals.RepairSuffixMemo);
                        var candidateMemo=default(NavRecastIntervals.RepairSuffixMemo);
                        var fallback=default(NavRecastIntervals.RepairRejoinFallback);
                        int control=0,candidate=0;
                        for(int repeat=0;repeat<3;repeat++)
                        for(int exit=actor.route.Length-1;exit>=0;exit--)
                        {
                            int start=NavRecastIntervals.TraceObservationsForTests;
                            bool original=NavRecastIntervals.TryContinuationRejoinFromPrefix(route,after,at,actor.route,
                                exit,goal,reservation,out int expected,frame.Working,ref originalMemo,actor.Id);
                            control+=NavRecastIntervals.TraceObservationsForTests-start;
                            start=NavRecastIntervals.TraceObservationsForTests;
                            bool actual=NavRecastIntervals.TryContinuationRejoinFromPrefix(route,after,at,actor.route,
                                exit,goal,reservation,out int selected,frame.Working,ref candidateMemo,ref fallback,actor.Id);
                            candidate+=NavRecastIntervals.TraceObservationsForTests-start;
                            Assert.False(original);
                            Assert.AreEqual((original,expected),(actual,selected));
                        }
                        Assert.True(fallback.Scanned);
                        Assert.Less(candidate,control);
                        TestContext.WriteLine($"DETOUR_FALLBACK_MAP map={map} exits={actor.route.Length} traces={control}->{candidate}");
                    }
                    finally { frame.Release(); }
                    scope.Discard();
                }
            }
            finally
            {
                session.ReleaseCommittedHostRoot();
                session.DestroyNavigationAuthority();
                Assert.Zero(budget.PhysicalBytes);
                Assert.Zero(budget.HeldBytes);
                Assert.Zero(budget.ActiveScopes);
            }
        }

        [Test] public void HeldSweepReadsTheCurrentFrameAfterQueryScopeEnds()
        {
            var session = Ready();
            var budget = session.PhysicalHostBudget;
            try {
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission, 801, 1, out var scope));
                using (scope) {
                    var frame = Frame(session, scope.OwnedReservation());
                    try {
                        var mover = frame.After[0];
                        var blocker = frame.After[1];
                        blocker.Position = mover.Position;
                        blocker.Location = mover.Location;
                        var authority = (INavRecastSweepAuthority)frame.Working;
                        bool Clear() => authority.AllowsRecastSweep(mover.Id, mover.Location, mover.Position, mover.Radius);
                        using (frame.Working.ObserveHeldForQueries(true)) Assert.True(Clear());
                        blocker.Held = true;
                        Assert.False(Clear());
                        using (frame.Working.ObserveHeldForQueries(true)) Assert.False(Clear());
                    } finally { frame.Release(); }
                }
            } finally {
                session.ReleaseCommittedHostRoot();
                session.DestroyNavigationAuthority();
                Assert.Zero(budget.PhysicalBytes);
                Assert.Zero(budget.HeldBytes);
                Assert.Zero(budget.ActiveScopes);
            }
        }

        [Test] public void AcceptedMotorTargetReusesTheSameLiveSweepAndPublishesIdentically()
        {
            var session=Ready(crimson:true);
            var budget=session.PhysicalHostBudget;
            try{
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope){
                    var r=scope.OwnedReservation();
                    NavPreparedCrowdStep legacy=null,reused=null;
                    NavMotionTranscript original=null,current=null;
                    try{
                        legacy=Frame(session,r);
                        reused=Frame(session,r);
                        NavCrowd.CoveredRouteSweepsForTests=0;
                        legacy.Compute(1d/30);
                        Assert.Zero(NavCrowd.CoveredRouteSweepsForTests);
                        reused.AllowRepairCertificateReuse=true;
                        reused.Compute(1d/30);
                        Assert.Greater(NavCrowd.CoveredRouteSweepsForTests,0);
                        original=NavMotionTranscript.Create(legacy,801,1,r);
                        current=NavMotionTranscript.Create(reused,801,1,r);
                        Same(original,current);
                        TestContext.WriteLine("ROUTE_SWEEPS_REUSED "+NavCrowd.CoveredRouteSweepsForTests);
                    }finally{
                        NavCrowd.CoveredRouteSweepsForTests=0;
                        current?.Release();original?.Release();reused?.Release();legacy?.Release();
                    }
                }
            }finally{
                session.ReleaseCommittedHostRoot();
                session.DestroyNavigationAuthority();
                Assert.Zero(budget.PhysicalBytes);
                Assert.Zero(budget.HeldBytes);
            }
        }
        [Test] public void UnboundReceiversShareOnlyEmptyPaidTracesAndMatchFullConstruction()
        {
            var session=Ready(independent:true,third:true);
            var budget=session.PhysicalHostBudget;
            try{
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope){
                    var r=scope.OwnedReservation();
                    NavPreparedCrowdStep frame=null;
                    NavMotionTranscript independent=null,shared=null;
                    try{
                        frame=Frame(session,r);
                        for(int i=0;i<2;i++){
                            frame.RecastRoutes[i].Release();
                            frame.RecastRoutes[i]=null;
                            frame.RecastReceipts[i]=default;
                        }
                        long physical=budget.PhysicalBytes;
                        independent=NavMotionTranscript.Create(frame,801,1,r);
                        long independentBytes=budget.PhysicalBytes-physical;
                        physical=budget.PhysicalBytes;
                        shared=NavMotionTranscript.Create(frame,801,1,r,shareEmptyTraces:true);
                        long sharedBytes=budget.PhysicalBytes-physical;
                        Same(independent,shared);
                        Assert.AreSame(shared[0].Chord,shared[0].Remaining);
                        Assert.AreSame(shared[0].Chord,shared[1].Chord);
                        Assert.AreEqual(NavMotionContinuation.NoAcceptedRoute,shared[0].Continuation);
                        Assert.AreEqual(NavMotionContinuation.Ready,shared[2].Continuation);
                        Assert.AreNotSame(shared[0].Chord,shared[2].Chord);
                        Assert.Less(sharedBytes,independentBytes);
                        TestContext.WriteLine("MOTION_EMPTY_PAID independent="+independentBytes+" shared="+sharedBytes);
                    }finally{shared?.Release();independent?.Release();frame?.Release();}
                }
            }finally{
                session.ReleaseCommittedHostRoot();
                session.DestroyNavigationAuthority();
                Assert.Zero(budget.PhysicalBytes);
                Assert.Zero(budget.HeldBytes);
            }
        }
        [TestCase("none"),TestCase("move"),TestCase("local"),TestCase("held"),TestCase("order"),TestCase("mobility"),TestCase("geometry"),TestCase("global"),TestCase("request"),TestCase("incarnation"),TestCase("endpoint"),TestCase("typed"),TestCase("bad-chord"),TestCase("bad-local")]
        [TestCase("none",true),TestCase("move",true),TestCase("local",true),TestCase("held",true),TestCase("order",true),TestCase("mobility",true),TestCase("geometry",true),TestCase("global",true),TestCase("request",true),TestCase("incarnation",true),TestCase("endpoint",true),TestCase("typed",true),TestCase("bad-chord",true),TestCase("bad-local",true)]
        public void RetainedTailMatchesFreshAndRejectsInvalidation(string mutation,bool crimson=false)
        {
            var s=Ready(crimson:crimson);var budget=s.PhysicalHostBudget;long before=budget.PhysicalBytes;
            try{
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope){var r=scope.OwnedReservation();NavMotionProofMemo memo=null;NavPreparedCrowdStep first=null,second=null;NavMotionTranscript original=null,fresh=null,reused=null;
                    try{
                        first=Frame(s,r);original=NavMotionTranscript.Create(first,801,1,r);Assert.True(Enumerable.Range(0,original.Count).All(i=>original[i].Continuation==NavMotionContinuation.Ready));
                        memo=NavMotionProofMemo.TryCreate(first,original,r);Assert.NotNull(memo,"Fixture must contain a nonempty fixed tail");
                        // Only memo-owned immutable edges survive the first frame.
                        original.Release();original=null;first.Release();first=null;
                        second=Frame(s,r,mutation=="move"||mutation=="local",mutation=="local");
                        if(mutation=="request")foreach(var a in second.After)a.InstalledRequest++;
                        if(mutation=="incarnation")foreach(var a in second.After)a.Incarnation++;
                        if(mutation=="endpoint")foreach(var a in second.After)a.GoalLocation=a.Location;
                        if(mutation=="typed")for(int i=0;i<second.RecastRoutes.Length;i++){second.RecastRoutes[i].Release();second.RecastRoutes[i]=null;}
                        if(mutation=="bad-chord")foreach(var a in second.After)a.Position=new NavPoint(1000,1000);
                        if(mutation=="bad-local")foreach(var a in second.After)a.localRoute=new[]{new NavPoint(1000,1000)};
                        if(mutation=="held")second.After[0].Held=true;
                        if(mutation=="order")foreach(var a in second.After)a.InstalledOrder++;
                        if(mutation=="mobility")foreach(var a in second.After)a.MobilityRevision++;
                        if(mutation=="geometry"){second.GeometryPin.Release();second.GeometryPin=second.ProfilePin;second.GeometryPin.Retain();}
                        if(mutation=="global")foreach(var a in second.After)a.route=(NavPoint[])a.route.Clone();
                        // The no-op observer forces the conservative provider path, including
                        // full suffix advancement, without changing any geometric answer.
                        var hook=TerrainLocations.IndexedCutCandidateForTests;
                        try{if(crimson)TerrainLocations.IndexedCutCandidateForTests=_=>{};fresh=NavMotionTranscript.Create(second,801,2,r);}
                        finally{TerrainLocations.IndexedCutCandidateForTests=hook;}
                        reused=NavMotionTranscript.Create(second,801,2,r,memo);Same(fresh,reused);
                        if(mutation=="none"||mutation=="move"||mutation=="local"){
                            Assert.Greater(reused.ReusedTails,0,"A positive reuse path must actually execute");
                            int skipped=Enumerable.Range(0,original?.Count??reused.Count).Sum(i=>reused[i].ReusedTail?reused[i].Remaining.Count-reused[i].TailStart:0);
                            Assert.Greater(skipped,0);TestContext.WriteLine("IMMUTABLE_TAIL_CONTROL fresh receivers="+fresh.Count+" reused="+reused.ReusedTails+" fixed legs="+skipped+" actual_move="+(mutation!="none"));
                        }else Assert.Zero(reused.ReusedTails,"A stale/static assumption cannot authorize a new tail");
                    }finally{reused?.Release();fresh?.Release();second?.Release();memo?.Release();original?.Release();first?.Release();}
                    scope.Discard();
                }
                Assert.AreEqual(before,budget.PhysicalBytes,"Cache may retain no orphaned physical liability after release");Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        private sealed class AuditAuthority : INavigationMotionAuthority
        {
            internal bool Refuse;internal int Committed,Refused,RefuseReceiver,SecondRefusedReceiver;
            private readonly NavigationSession session;
            internal AuditAuthority(NavigationSession session){this.session=session;session.BindMotionAuthority(this);}
            public void ReconcilePendingSource(NavigationSession.MotionPublication publication) {}
            public NavigationMotionAdoption Prepare(NavigationSession.MotionPublication publication)
            {
                if(Refuse){Refused++;throw new InvalidOperationException("expected publication abort");}
                return new Adoption(this,publication,publication.AcceptSource());
            }
            private sealed class Adoption : NavigationMotionAdoption
            {
                private readonly AuditAuthority owner;private readonly NavigationSession.MotionPublication publication;private NavigationSession.MotionSourcePin pin;
                internal Adoption(AuditAuthority owner,NavigationSession.MotionPublication publication,NavigationSession.MotionSourcePin pin){this.owner=owner;this.publication=publication;this.pin=pin;}
                internal override void ValidateCurrent()
                {
                    Assert.False(publication.Committed);
                    Assert.False(publication.Aborted);
                    for(int i=0;i<publication.Transcript.Count;i++){
                        var row=publication.Transcript[i];
                        if((row.Before.Entity!=owner.RefuseReceiver&&row.Before.Entity!=owner.SecondRefusedReceiver)||
                            row.Before.Location.Equals(row.After.Location)&&row.Before.Heading==row.After.Heading)continue;
                        owner.Refused++;
                        throw new InvalidOperationException("Receiver policy refusal.",new GroundMotionRefusal(row.Before.Entity,"expected receiver denial"));
                    }
                }
                internal override void ApplyCommittedNoThrow(){owner.Committed++;}
                public override void Dispose(){if(pin==null)return;try{Assert.True(owner.session.TryRetireMotionSource(pin));}finally{pin.Dispose();pin=null;}}
            }
        }
        [Test] public void ReceiverPolicyDenialLeavesOnlyThatReceiverStationary()
        {
            var s=Ready(independent:true);var budget=s.PhysicalHostBudget;
            try{
                var audit=new AuditAuthority(s);
                var actors=s.Crowd.Units.ToArray();
                var refused=actors[0];var independent=actors[1];
                bool independentStep=false;
                for(int i=0;i<90&&!independentStep;i++){
                    var first=refused.Position;var second=independent.Position;
                    s.Step(1d/30);
                    independentStep=!first.Equals(refused.Position)&&!second.Equals(independent.Position);
                }
                Assert.True(independentStep,"Both independently routed bodies must be translating before the denial.");
                var pose=(refused.Location,refused.Heading,refused.InstalledRequest,refused.Moving,refused.Held);
                var other=independent.Position;
                audit.RefuseReceiver=refused.Id;
                int publications=s.OwnedMovementPublications;
                s.Step(1d/30);
                Assert.AreEqual(1,audit.Refused);
                Assert.AreEqual(publications+1,s.OwnedMovementPublications);
                Assert.AreEqual(pose,(refused.Location,refused.Heading,refused.InstalledRequest,refused.Moving,refused.Held));
                Assert.AreNotEqual(other,independent.Position);
                Assert.Zero(budget.HeldBytes);
                Assert.Zero(budget.ActiveScopes);
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);}
        }

        [Test]
        public void TwoReceiverDenialsCannotBlockAnIndependentThirdRoute()
        {
            var s=Ready(independent:true,third:true);var budget=s.PhysicalHostBudget;
            try{
                var audit=new AuditAuthority(s);var actors=s.Crowd.Units.ToArray();
                bool allMoving=false;
                for(int i=0;i<90&&!allMoving;i++){
                    var before=actors.Select(x=>x.Position).ToArray();
                    s.Step(1d/30);
                    allMoving=actors.Where((a,j)=>!a.Position.Equals(before[j])).Count()==actors.Length;
                }
                Assert.True(allMoving,"All three independent routes must translate before fault injection.");
                audit.RefuseReceiver=actors[0].Id;audit.SecondRefusedReceiver=actors[1].Id;
                int publications=s.OwnedMovementPublications;
                var position=actors[2].Position;
                var withheld=actors.Take(2).Select(a=>(a.Location,a.Heading,a.InstalledRequest,a.Moving,a.Held)).ToArray();
                s.Step(1d/30);
                Assert.AreEqual(2,audit.Refused,"Both per-receiver denials must actually execute.");
                CollectionAssert.AreEqual(withheld,actors.Take(2).Select(a=>(a.Location,a.Heading,a.InstalledRequest,a.Moving,a.Held)));
                Assert.Zero(budget.HeldBytes);
                Assert.Zero(budget.ActiveScopes);
                Assert.AreEqual(publications+1,s.OwnedMovementPublications,"A safe frame must publish after both independently refused proposals.");
                Assert.AreNotEqual(position,actors[2].Position);
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);}
        }

        [TestCase(false),TestCase(true)] public void ProductionMemoCommitAbortAndCapturePreserveOwnedStateAndRelease(bool crimson)
        {
            var s=Ready(crimson:crimson);var budget=s.PhysicalHostBudget;
            byte[] Bytes(){using var state=s.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                var audit=new AuditAuthority(s);for(int i=0;i<3;i++)s.Step(1d/30);
                Assert.Greater(audit.Committed,0);Assert.Greater(s.MotionProofHits,0);
                var before=s.Crowd.Units.Select(a=>(a.Position,a.Heading,a.InstalledRequest,a.Outcome)).ToArray();
                audit.Refuse=true;s.Step(1d/30);Assert.Greater(audit.Refused,0,"The real preparation abort must execute");Assert.AreEqual(6,s.LastOwnedMovementRefusal);Assert.AreEqual("expected publication abort",s.LastOwnedMovementRefusalMessage);
                CollectionAssert.AreEqual(before,s.Crowd.Units.Select(a=>(a.Position,a.Heading,a.InstalledRequest,a.Outcome)).ToArray());
                var bytes=Bytes();CollectionAssert.AreEqual(bytes,Bytes(),"Derived memo never changes captured authoritative fields");
                audit.Refuse=false;s.Step(1d/30);Assert.Greater(audit.Committed,3);
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        [Test] public void SectionRejectsBlockersSupportEdgesAndInsufficientBodyClearance()
        {
            var floor=new MapSupport("floor",new NavObstacle(-20,-20,20,20),false,surfaceId:"floor");
            var platform=new MapSupport("deck",new NavObstacle(1,1,2,2),true,surfaceId:"deck");
            int Section(MapSupport[] regions,NavObstacle[] blockers,NavPoint start,NavPoint goal){
                var terrain=new TerrainLocations(new NavGeometry(20,blockers,1),regions,Array.Empty<NavSurfaceTransition>());
                return terrain.ClearSectionSupport(new NavLocation(start,"floor"),new NavLocation(goal,"floor"),.5);
            }
            var a=new NavPoint(-4,-4);var b=new NavPoint(4,4);
            Assert.GreaterOrEqual(Section(new[]{floor},Array.Empty<NavObstacle>(),a,b),0);
            Assert.Less(Section(new[]{platform,floor},Array.Empty<NavObstacle>(),a,b),0);
            Assert.Less(Section(new[]{floor},new[]{new NavObstacle(4.3,3,5,4)},a,b),0);
            Assert.Less(Section(new[]{floor},Array.Empty<NavObstacle>(),a,new NavPoint(19.9,4)),0);
        }

        [Test] public void CertifiedSectionMatchesFullTraceAndContinuesExactlyAfterRestore()
        {
            var map=new ProvingGroundMap();
            var hot=Ready(independent:true,terrain:map);
            NavigationSession cold=null;
            var previous=NavMotionExecutor.Current;
            using var executor=new NavMotionExecutor(2);
            byte[] Bytes(NavigationSession session){
                using var state=session.CaptureState();
                return WorldWire.Pack(w=>WorldWire.Write(w,state));
            }
            try{
                new AuditAuthority(hot);
                NavMotionExecutor.Current=executor;
                long initial=NavRecastIntervals.SectionObservationsForTests;
                for(int i=0;i<3;i++)hot.Step(1d/30);
                Assert.Greater(NavRecastIntervals.SectionObservationsForTests,initial);
                var saved=Bytes(hot);
                var profile=PlayableProfile.Create(PlayableProfile.RecastGroupCorridorDefault.CopyData(),map);
                cold=new NavigationSession(801,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),
                    profile.Navigation,terrain:map){UsesRecastGroundRoutes=true};
                cold.ConfigureMarch(4,profile.Revision);
                cold.RestoreState(WorldWire.Unpack(saved,WorldWire.ReadNavigationSessionState));
                new AuditAuthority(cold);CollectionAssert.AreEqual(saved,Bytes(cold));
                for(int tick=0;tick<5;tick++){
                    NavTypedRoute.DisableClearSectionForTests=false;hot.Step(1d/30);
                    NavTypedRoute.DisableClearSectionForTests=true;cold.Step(1d/30);
                    CollectionAssert.AreEqual(Bytes(hot),Bytes(cold));
                }
            }finally{
                NavTypedRoute.DisableClearSectionForTests=false;NavMotionExecutor.Current=previous;
                foreach(var session in new[]{hot,cold})if(session!=null){
                    session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
                    Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
                }
            }
        }

        [Test] public void WorkerAndSerialBuildPublishIdenticalOrderedLegs()
        {
            var session=Ready(crimson:true);
            var previous=NavMotionExecutor.Current;
            using var executor=new NavMotionExecutor(2);
            try{
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope){
                    var reservation=scope.OwnedReservation();
                    var frame=Frame(session,reservation,move:true);
                    NavMotionTranscript serial=null,parallel=null;
                    try{
                        serial=NavMotionTranscript.Create(frame,801,1,reservation);
                        NavMotionExecutor.Current=executor;
                        long legs=NavTranscriptTraceBatch.PublishedLegsForTests;
                        parallel=NavMotionTranscript.Create(frame,801,1,reservation,shareEmptyTraces:true);
                        Same(serial,parallel);
                        Assert.Greater(NavTranscriptTraceBatch.PublishedLegsForTests,legs);
                        Assert.True(executor.IsIdle);
                    }finally{parallel?.Release();serial?.Release();frame.Release();}
                }
            }finally{
                NavMotionExecutor.Current=previous;
                session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
                Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);
            }
        }

        [Test] public void WorkerTranscriptMatchesSerialAfterRestoreAndReleasesScratch()
        {
            var hot=Ready(crimson:true);
            NavigationSession cold=null;
            var previous=NavMotionExecutor.Current;
            byte[] Bytes(NavigationSession session)
            {
                using var state=session.CaptureState();
                return WorldWire.Pack(w=>WorldWire.Write(w,state));
            }
            using var executor=new NavMotionExecutor(2);
            try{
                var committed=new AuditAuthority(hot);
                NavMotionExecutor.Current=executor;
                long initial=NavTranscriptTraceBatch.PublishedLegsForTests;
                for(int i=0;i<5;i++)hot.Step(1d/30);
                Assert.Greater(NavTranscriptTraceBatch.PublishedLegsForTests,initial);
                Assert.Greater(committed.Committed,0);
                var saved=Bytes(hot);
                var map=new CrimsonGroveMap();
                cold=new NavigationSession(801,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),
                    PlayableProfile.Default.Navigation,terrain:map){UsesRecastGroundRoutes=true};
                cold.ConfigureMarch(4,PlayableProfile.Default.Revision);
                cold.RestoreState(WorldWire.Unpack(saved,WorldWire.ReadNavigationSessionState));
                new AuditAuthority(cold);
                CollectionAssert.AreEqual(saved,Bytes(cold));
                for(int i=0;i<4;i++){
                    NavMotionExecutor.Current=executor;hot.Step(1d/30);
                    NavMotionExecutor.Current=null;cold.Step(1d/30);
                    CollectionAssert.AreEqual(Bytes(hot),Bytes(cold),"serial/worker continuation "+i);
                }
            }finally{
                NavMotionExecutor.Current=previous;
                foreach(var session in new[]{cold,hot})if(session!=null){
                    session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
                    Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);
                    Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
                }
            }
        }

        [Test] public void CrimsonCurrentSaveRestoresAndContinuesWithWarmProofsExactly()
        {
            var hot=Ready(crimson:true);NavigationSession cold=null;
            byte[] Bytes(NavigationSession value){using var state=value.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                new AuditAuthority(hot);for(int i=0;i<5;i++)hot.Step(1d/30);
                Assert.Greater(hot.MotionProofHits,0);
                var saved=Bytes(hot);var map=new CrimsonGroveMap();
                cold=new NavigationSession(801,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),PlayableProfile.Default.Navigation,terrain:map){UsesRecastGroundRoutes=true};
                cold.ConfigureMarch(4,PlayableProfile.Default.Revision);
                cold.RestoreState(WorldWire.Unpack(saved,WorldWire.ReadNavigationSessionState));
                new AuditAuthority(cold);CollectionAssert.AreEqual(saved,Bytes(cold));
                for(int i=0;i<4;i++){hot.Step(1d/30);cold.Step(1d/30);CollectionAssert.AreEqual(Bytes(hot),Bytes(cold),"current-format exact continuation "+i);}
            }finally{
                foreach(var session in new[]{cold,hot})if(session!=null){session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
            }
        }

        [TestCase(false),TestCase(true)]
        public void LayeredMapMotionKeepsExactStateAndTracesWithCertifiedReuse(bool antennaCity)
        {
            IPlayableTerrain map=antennaCity?(IPlayableTerrain)new AntennaCityMap():new MossHighlandMap();
            var conservative=Ready(terrain:map);
            NavigationSession optimized=null;
            var hook=TerrainLocations.IndexedCutCandidateForTests;
            try{
                optimized=Ready(terrain:map);
                var expected=new AuditAuthority(conservative);var actual=new AuditAuthority(optimized);
                byte[] State(NavigationSession value){using var state=value.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,state));}
                for(int i=0;i<12;i++){
                    try{TerrainLocations.IndexedCutCandidateForTests=_=>{};conservative.Step(1d/30);}
                    finally{TerrainLocations.IndexedCutCandidateForTests=hook;}
                    optimized.Step(1d/30);
                    CollectionAssert.AreEqual(conservative.Crowd.Units.Select(u=>(u.Location,u.Heading,u.Goal,u.Outcome,u.InstalledRequest)).ToArray(),
                        optimized.Crowd.Units.Select(u=>(u.Location,u.Heading,u.Goal,u.Outcome,u.InstalledRequest)).ToArray(),"Physical state at tick "+i);
                    Assert.AreEqual(expected.Committed,actual.Committed);
                }
                Assert.Greater(actual.Committed,0);
                CollectionAssert.AreEqual(State(conservative),State(optimized),"Exact current-format state after warm movement.");
                Assert.Greater(optimized.MotionProofHits,0,"The accepted portal route must use the retained typed tail.");
                Assert.Zero(conservative.MotionProofHits);
            }finally{
                TerrainLocations.IndexedCutCandidateForTests=hook;
                foreach(var session in new[]{conservative,optimized})if(session!=null){
                    session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
                    Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);
                }
            }
        }

        [TestCase(false),TestCase(true)] public void TranscriptIdentityDoesNotAuthorizeDifferentActualMotion(bool crimson)
        {
            var s=Ready(crimson:crimson);var budget=s.PhysicalHostBudget;
            try{
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope){var r=scope.OwnedReservation();NavPreparedCrowdStep a=null,b=null;NavMotionTranscript first=null,moved=null;
                    try{
                        a=Frame(s,r);b=Frame(s,r,move:true);
                        first=NavMotionTranscript.Create(a,801,1,r);moved=NavMotionTranscript.Create(b,801,1,r);
                        Assert.True(AiCombatMicroMotionAuthority.SameTranscript(first,first));
                        Assert.False(AiCombatMicroMotionAuthority.SameTranscript(first,moved));
                        Assert.False(AiCombatMicroMotionAuthority.SameTranscript(first,moved,compareOperation:false));
                    }finally{moved?.Release();first?.Release();b?.Release();a?.Release();}
                    scope.Discard();
                }
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }

    }
}

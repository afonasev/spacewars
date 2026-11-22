using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RecastTypedSweepTests
    {
        private sealed class OpenCurrentTargetClearance : INavMotionClearance
        { public bool ClearMotion(int actor,NavPoint end)=>true; }
        [TestCase(1)][TestCase(-1)]
        public void CurrentGuideOwnsAvoidanceProgressWhileFinalEndpointKeepsReverseAuthority(int side)
        {
            var body=new NavMotionBody{Id=1,Position=new NavPoint(0,0),Target=new NavPoint(0,side*2),Heading=0,Radius=.58,Speed=4,TurnSpeed=7.5,Active=true,OwnMoving=true,ReverseDistance=3,ReverseSpeedRatio=.5,ReverseConeRadians=.4};
            var final=new NavPoint(0,-side*2);var bodies=new[]{body};var neighbors=new int[NavCrowdMotion.NeighborCapacity];var clear=new OpenCurrentTargetClearance();var profile=PlayableProfile.RecastGroupCorridorDefault.Navigation;
            var turn=NavCrowdMotion.Select(bodies,0,neighbors,0,profile,1d/30,clear,final,false,body.Target);
            Assert.True(turn.IndependentTurn,"A reachable turn toward the current guide must retain its progress even when the final endpoint is behind");
            Assert.AreEqual(body.Position,turn.Position);Assert.That(Math.Abs(NavCrowdMotion.Angle(turn.Heading-body.Heading)),Is.LessThanOrEqualTo(body.TurnSpeed/30));
            bool moved=false;for(int tick=0;tick<16;tick++){
                var before=bodies[0];var next=NavCrowdMotion.Select(bodies,0,neighbors,0,profile,1d/30,clear,final,true,before.Target);
                if(!next.Position.Equals(before.Position)){Assert.Greater(side*(next.Position.Z-before.Position.Z),0,"Current guide must not enable reverse toward the different final endpoint");moved=true;}
                bodies[0].Position=next.Position;bodies[0].Heading=next.Heading;
            }
            Assert.True(moved,"The same current objective must turn then translate, not alternate indefinitely");
        }
        [TestCase(false)][TestCase(true)]
        public void PreparedFormationDomainRetainsFreshFlagsLimitsEndpointsAndReceiverExemption(bool directional)
        {
            var source=Ready();try{
                var route=Route(source);int portal=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);
                Assert.True(RecastFormationMotion.Axis(route,portal,out var origin,out _,out var axis,out double length));Assert.True(RecastFormationMotion.Bounds(route,portal,origin,axis,out double low,out double high,out double radius));
                var normal=new NavPoint(-axis.Z,axis.X);double reach=source.Crowd.Profile.LocalDetourDistance;
                var seed=new NavFormationFrameHint{Flags=(byte)(NavFormationFrameHint.Bound|(directional?NavFormationFrameHint.Directional:0)),TargetLeg=portal,DirectionalBankReach=reach,ForwardLimit=length/2};
                var prepared=new RecastFormationMotion.PreparedDomain(seed,route);Assert.True(prepared.IsPrepared);
                var points=(from station in new[]{-radius-reach,-radius,0,length/2,length+radius,length+radius+reach} from side in new[]{low-radius,low,(low+high)/2,high,high+radius} select new NavPoint(origin.X+axis.X*station+normal.X*side,origin.Z+axis.Z*station+normal.Z*side)).ToArray();
                foreach(byte flags in new byte[]{0,1,4,64,68,70,76})foreach(double limit in new[]{-radius,length/2})foreach(bool receiver in new[]{false,true})foreach(var from in points)foreach(var to in points){
                    var live=seed;live.Flags=flags;live.ForwardLimit=limit;live.DirectionalBankReach=directional?0:reach;
                    Assert.AreEqual(RecastFormationMotion.Allows(live,route,from,to,receiver),prepared.Allows(live,route,from,to,receiver),"Prepared facts must not cache a decision or old frame flags/limit");
                }
                var changed=seed;changed.TargetLeg=-1;Assert.AreEqual(RecastFormationMotion.Allows(changed,route,points[0],points[points.Length-1]),prepared.Allows(changed,route,points[0],points[points.Length-1]));
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();Assert.Zero(Budget(source).PhysicalBytes);Assert.Zero(Budget(source).HeldBytes);Assert.Zero(Budget(source).ActiveScopes);}
        }
        [Test] public void FixedSuffixKeepsEveryPhysicalLegAndPortalAcrossProjectionElision()
        {
            var source=Ready();try{
                var route=Route(source);Assert.True(Budget(source).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope){var r=scope.OwnedReservation();Assert.True(NavRecastIntervals.CanTraceRemaining(route,r,source.Crowd));
                    var at=route.Legs[0].From;var state=NavRecastIntervals.Initial(route);int cursor=state.Cursor,checkedLinks=0;
                    // Include legal seams, terminal fan, stationary chords and a
                    // rejected wrong-bank shortcut; retain independent cursors.
                    var targets=route.Legs.Where(l=>l.Kind==NavTypedLegKind.Surface).Select(l=>l.To.Position).ToArray();
                    foreach(var target in targets.Concat(targets.Reverse()).Concat(targets)){
                        bool expected=NavRecastIntervals.AdvanceOwned(route,state,at,target,r,out var next,out var end,out var full,source.Crowd,2);
                        bool actual=NavRecastIntervals.TryTraceRemainingOwned(route,cursor,at,target,r,out var reached,out var nextCursor,out var suffix,source.Crowd,2);
                        try{
                            Assert.AreEqual(expected,actual);if(!expected)continue;Assert.AreEqual(end,reached);Assert.AreEqual(full.Count,suffix.Count);
                            for(int i=0;i<full.Count;i++){Assert.AreEqual(full[i].Kind,suffix[i].Kind);Assert.AreEqual(full[i].From,suffix[i].From);Assert.AreEqual(full[i].To,suffix[i].To);Assert.AreEqual(full[i].PortalId,suffix[i].PortalId);Assert.AreEqual(full[i].Cost,suffix[i].Cost);}
                            state=next.After;cursor=nextCursor;at=end;checkedLinks++;
                        }finally{(full as NavOwnedStorage)?.ReleaseOwner();(suffix as NavOwnedStorage)?.ReleaseOwner();}
                    }
                    Assert.Greater(checkedLinks,targets.Length);
                }
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();Assert.Zero(Budget(source).PhysicalBytes);Assert.Zero(Budget(source).HeldBytes);}
        }
        [Test] public void ObservedFormationLayoutMatchesOwnedTraceAcrossLanesAndBankReach()
        {
            var source=Ready();var budget=Budget(source);long bytes=budget.PhysicalBytes;
            try{
                var route=Route(source);var portals=Enumerable.Range(0,route.Legs.Count).Where(i=>route.Legs[i].Kind==NavTypedLegKind.Portal).ToArray();int accepted=0,refused=0;
                RecastFormationGuide.TraceObservationsForTests=0;
                for(int i=1;i<portals.Length;i++)foreach(double pitch in new[]{route.RecastPlan.Spine.Radius,source.Crowd.Profile.ArrivalSlotSpacing,source.Crowd.Profile.ArrivalSlotSpacing*2})foreach(double reach in new[]{0,.01,2,40}){
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();string entry=route.Legs[portals[i-1]].PortalId,exit=route.Legs[portals[i]].PortalId;
                        RecastFormationGuide.DisableTraceObservationForTests=true;bool expected=RecastFormationGuide.TryLayout(route,entry,exit,pitch,r,out var oldLayout,reach);
                        RecastFormationGuide.DisableTraceObservationForTests=false;bool actual=RecastFormationGuide.TryLayout(route,entry,exit,pitch,r,out var layout,reach);
                        Assert.AreEqual(expected,actual);Assert.AreEqual(oldLayout,layout,"Direction, bounds, lane count and bank fallback remain exact");if(actual)accepted++;else refused++;
                    }
                    Assert.AreEqual(bytes,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
                }
                Assert.Greater(accepted,0);Assert.Greater(refused,0);Assert.Greater(RecastFormationGuide.TraceObservationsForTests,0);
            }finally{RecastFormationGuide.DisableTraceObservationForTests=false;RecastFormationGuide.TraceObservationsForTests=0;source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        [Test] public void ObservedTraceKeepsExactReceiptsTargetsLiveHoldAndReleasesScratch()
        {
            var source=Ready();var budget=Budget(source);long bytes=budget.PhysicalBytes;
            try{
                var route=Route(source);var at=route.Legs[0].From;var state=NavRecastIntervals.Initial(route);
                var targets=route.Legs.Where(l=>l.Kind==NavTypedLegKind.Surface).Select(l=>l.To.Position).ToArray();int accepted=0,refused=0;
                NavRecastIntervals.TraceObservationsForTests=0;
                NavRecastIntervals.SurfaceConnectionsForTests=0;
                foreach(var target in targets.Concat(targets.Reverse()).Concat(targets).Concat(new[]{at.Position,new NavPoint(4000,4000)})){
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                    using(scope){var r=scope.OwnedReservation();
                        NavRecastIntervals.DisableTraceObservationForTests=true;bool expected=NavRecastIntervals.Advance(route,state,at,target,r,out var oldReceipt,source.Crowd,2);
                        NavRecastIntervals.DisableTraceObservationForTests=false;bool actual=NavRecastIntervals.Advance(route,state,at,target,r,out var receipt,source.Crowd,2);
                        Assert.AreEqual(expected,actual);Assert.AreEqual(oldReceipt,receipt,"All receipt fields, including rejected prefixes, remain exact");
                        for(int i=-1;i<=targets.Length+1;i++){
                            NavRecastIntervals.DisableTraceObservationForTests=true;bool legal=NavRecastIntervals.CanTarget(route,state,at,i,r,source.Crowd,2);
                            NavRecastIntervals.DisableTraceObservationForTests=false;Assert.AreEqual(legal,NavRecastIntervals.CanTarget(route,state,at,i,r,source.Crowd,2));
                        }
                        if(actual){accepted++;at=new NavLocation(target,receipt.ToSurface);state=receipt.After;}else refused++;
                    }
                    Assert.AreEqual(bytes,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
                }
                Assert.Greater(accepted,0);Assert.Greater(refused,0);Assert.Greater(NavRecastIntervals.TraceObservationsForTests,0,"Production observation must execute, not silently fall back");
                Assert.Greater(NavRecastIntervals.SurfaceConnectionsForTests,0,"The observed receipt must include the surface-only connector path.");
                // Existing HOLD oracle below exercises live peers; the observed
                // path must still ask the actual authority for each fresh sweep.
                source.Crowd.TryGet(1,out var peer);var position=peer.Position;var location=peer.Location;bool held=peer.Held;
                try{
                    peer.Position=route.Subscription.Origin.Position;peer.Location=route.Subscription.Origin;peer.Held=true;
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var initial=NavRecastIntervals.Initial(route);
                        NavRecastIntervals.DisableTraceObservationForTests=true;bool old=NavRecastIntervals.Advance(route,initial,route.Subscription.Origin,targets[0],r,out var oldReceipt,source.Crowd,2);
                        NavRecastIntervals.DisableTraceObservationForTests=false;Assert.AreEqual(old,NavRecastIntervals.Advance(route,initial,route.Subscription.Origin,targets[0],r,out var receipt,source.Crowd,2));Assert.AreEqual(oldReceipt,receipt);
                    }
                }finally{peer.Position=position;peer.Location=location;peer.Held=held;}
            }finally{NavRecastIntervals.DisableTraceObservationForTests=false;NavRecastIntervals.TraceObservationsForTests=0;NavRecastIntervals.SurfaceConnectionsForTests=0;source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        [Test] public void RetainedStepTraceKeepsExactProjectionAndReleasesItsPaidEdges()
        {
            var source=Ready();try{
                var route=Route(source);long bytes=Budget(source).PhysicalBytes;Assert.True(Budget(source).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope){var r=scope.OwnedReservation();var at=route.Legs[0].From;var state=NavRecastIntervals.Initial(route);
                    foreach(var leg in route.Legs.Where(l=>l.Kind==NavTypedLegKind.Surface)){
                        bool expected=NavRecastIntervals.Advance(route,state,at,leg.To.Position,r,out var prior,source.Crowd,2);
                        bool actual=NavRecastIntervals.AdvanceOwned(route,state,at,leg.To.Position,r,out var next,out var reached,out var trace,source.Crowd,2);
                        try{
                            Assert.AreEqual(expected,actual);if(!actual)continue;
                            Assert.AreEqual(prior.After.Phase,next.After.Phase);Assert.AreEqual(prior.After.Cursor,next.After.Cursor);Assert.AreEqual(prior.After.Station,next.After.Station);Assert.AreEqual(prior.After.HighWater,next.After.HighWater);
                            var end=at;foreach(var part in trace){Assert.AreEqual(end,part.From);end=part.To;}Assert.AreEqual(reached,end);Assert.AreEqual(leg.To.Position,reached.Position);
                            state=next.After;at=reached;
                        }finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
                    }
                }
                // The allocation scope owns the birth journal until resolution.
                Assert.AreEqual(bytes,Budget(source).PhysicalBytes,"Retaining a step trace cannot leave cache or provider liability behind after its scope closes");
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();Assert.Zero(Budget(source).PhysicalBytes);Assert.Zero(Budget(source).HeldBytes);}
        }
        [Test] public void RepairSuffixMemoKeepsExactStateSurfaceGoalAndLiveHoldGuards()
        {
            var source=Ready();try{
                var route=Route(source);source.Crowd.TryGet(2,out var actor);source.Crowd.TryGet(1,out var peer);var state=NavRecastIntervals.Initial(route);
                Assert.True(Budget(source).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var goal=actor.GoalLocation.Value;
                    var memo=new NavRecastIntervals.RepairSuffixMemo(route,actor.route,goal,r,source.Crowd,actor.Id);
                    Assert.True(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,goal,r,out int original,source.Crowd,actor.Id));
                    for(int n=0;n<2;n++){Assert.True(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,goal,r,out int selected,source.Crowd,ref memo,actor.Id));Assert.AreEqual(original,selected);}
                    Assert.Greater(memo.Hits,0,"Positive same-input suffix must actually be reused");int hits=memo.Hits;
                    Assert.False(NavRecastIntervals.TryContinuationRejoin(route,state,new NavLocation(actor.Position,"deck/-1/1"),Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,goal,r,out _,source.Crowd,ref memo,actor.Id));Assert.AreEqual(hits,memo.Hits);
                    Assert.False(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,new NavLocation(actor.Goal,"bank/west"),r,out _,source.Crowd,ref memo,actor.Id));Assert.AreEqual(hits,memo.Hits);
                    var oldPosition=peer.Position;var oldLocation=peer.Location;bool held=peer.Held;
                    try{peer.Position=goal.Position;peer.Location=goal;peer.Held=true;
                        Assert.False(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,goal,r,out _,source.Crowd,ref memo,actor.Id),"A positive immutable suffix must never cache a live HOLD waiver");Assert.AreEqual(hits,memo.Hits);
                    }finally{peer.Position=oldPosition;peer.Location=oldLocation;peer.Held=held;}
                    var alias=NavOwnedText.Copy(goal.SurfaceId,r);try{var otherOwnedGoal=new NavLocation(goal.Position,new NavNameReference(alias,NavNameRole.Text));Assert.True(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,otherOwnedGoal,r,out _,source.Crowd,ref memo,actor.Id));Assert.AreEqual(hits,memo.Hits,"Different owned goal name cannot reuse or poison the original context");}finally{alias.Release();}
                    Assert.True(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,goal,r,out _,source.Crowd,ref memo,actor.Id));Assert.Greater(memo.Hits,hits);scope.Discard();
                }
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();Assert.Zero(Budget(source).PhysicalBytes);Assert.Zero(Budget(source).HeldBytes);Assert.Zero(Budget(source).ActiveScopes);}
        }
        [Test]
        public void PreparedRepairPrefixKeepsRejoinOrderAndAvoidsRepeatedTrace()
        {
            var session=Ready();
            var budget=Budget(session);
            try
            {
                var route=Route(session);
                Assert.True(session.Crowd.TryGet(2,out var actor));
                Assert.True(session.Crowd.TryGet(1,out var peer));
                var state=NavRecastIntervals.Initial(route);
                var local=actor.route.Take(3).ToArray();
                var candidates=new[]{actor.routeIndex,actor.route.Length-2,actor.route.Length-1,actor.routeIndex};
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope)
                {
                    var r=scope.OwnedReservation();
                    int start=NavRecastIntervals.TraceObservationsForTests;
                    var expected=candidates.Select(index=>{
                        bool valid=NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,local,0,actor.route,
                            index,actor.GoalLocation.Value,r,out int selected,session.Crowd,actor.Id);
                        return (valid,selected);
                    }).ToArray();
                    int original=NavRecastIntervals.TraceObservationsForTests-start;
                    Assert.True(expected.Any(result=>result.valid));
                    start=NavRecastIntervals.TraceObservationsForTests;
                    Assert.True(NavRecastIntervals.TryPrepareContinuationPrefix(route,state,actor.Location,local,0,r,
                        session.Crowd,actor.Id,out var at,out var after));
                    var memo=default(NavRecastIntervals.RepairSuffixMemo);
                    for(int i=0;i<candidates.Length;i++)
                    {
                        bool valid=NavRecastIntervals.TryContinuationRejoinFromPrefix(route,after,at,actor.route,
                            candidates[i],actor.GoalLocation.Value,r,out int selected,session.Crowd,ref memo,actor.Id);
                        Assert.AreEqual(expected[i],(valid,selected));
                    }
                    int reused=NavRecastIntervals.TraceObservationsForTests-start;
                    TestContext.WriteLine($"TYPED_REPAIR_PREFIX checks={candidates.Length} traces={original}->{reused}");
                    Assert.Less(reused,original);
                    var oldPosition=peer.Position;
                    var oldLocation=peer.Location;
                    bool oldHeld=peer.Held;
                    try
                    {
                        peer.Position=local[0];
                        peer.Location=new NavLocation(local[0],actor.Location.SurfaceId);
                        peer.Held=true;
                        Assert.False(NavRecastIntervals.TryPrepareContinuationPrefix(route,state,actor.Location,local,0,r,
                            session.Crowd,actor.Id,out _,out _));
                    }
                    finally
                    {
                        peer.Position=oldPosition;
                        peer.Location=oldLocation;
                        peer.Held=oldHeld;
                    }
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

        [TestCase(false)]
        [TestCase(true)]
        public void SharedRepairFallbackKeepsExactRejoinOrderAndDoesNotRepeatFailedTail(bool heldGoal)
        {
            var session=Ready();
            var budget=Budget(session);
            try
            {
                var route=Route(session);
                Assert.True(session.Crowd.TryGet(2,out var actor));
                Assert.True(session.Crowd.TryGet(1,out var peer));
                var goal=actor.GoalLocation.Value;
                var state=NavRecastIntervals.Initial(route);
                if(heldGoal)
                {
                    peer.Position=goal.Position;
                    peer.Location=goal;
                    peer.Held=true;
                }
                var last=actor.route.Length-1;
                var exits=new[]{last,last-1,0,last-2,-1,last-1,last+1,1};
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope)
                {
                    var reservation=scope.OwnedReservation();
                    var controlMemo=default(NavRecastIntervals.RepairSuffixMemo);
                    var candidateMemo=default(NavRecastIntervals.RepairSuffixMemo);
                    var fallback=default(NavRecastIntervals.RepairRejoinFallback);
                    int controlTraces=0,candidateTraces=0;
                    bool sawFallback=false;
                    foreach(int exit in exits)
                    {
                        int traces=NavRecastIntervals.TraceObservationsForTests;
                        bool original=NavRecastIntervals.TryContinuationRejoinFromPrefix(route,state,actor.Location,
                            actor.route,exit,goal,reservation,out int expected,session.Crowd,ref controlMemo,actor.Id);
                        controlTraces+=NavRecastIntervals.TraceObservationsForTests-traces;
                        traces=NavRecastIntervals.TraceObservationsForTests;
                        bool actual=NavRecastIntervals.TryContinuationRejoinFromPrefix(route,state,actor.Location,
                            actor.route,exit,goal,reservation,out int selected,session.Crowd,ref candidateMemo,ref fallback,actor.Id);
                        candidateTraces+=NavRecastIntervals.TraceObservationsForTests-traces;
                        Assert.AreEqual((original,expected),(actual,selected),$"proposed exit {exit}");
                        if(heldGoal)Assert.False(actual);
                        sawFallback|=fallback.Scanned;
                    }
                    Assert.True(sawFallback);
                    TestContext.WriteLine($"TYPED_REPAIR_FALLBACK held={heldGoal} traces={controlTraces}->{candidateTraces}");
                    if(heldGoal)Assert.Less(candidateTraces,controlTraces);
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

        [Test] public void ClosedPhysicalTailDoesNotCertifyEveryLocalPrefix()
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;
            var map=profile.AuthoredMap;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var session=new NavigationSession(801,geometry,profile.Navigation,null,map){UsesRecastGroundRoutes=true};
            try
            {
                session.ConfigureMarch(4,profile.Revision);
                session.Crowd.Add(1,new NavPoint(26.8,42),.58,7,7.5,canYield:true);
                session.Crowd.Add(2,new NavPoint(26.8,45.6),.58,7,7.5,canYield:true);
                Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(-26.8,15),new NavPoint(-26.8,18.6)}));
                using(var host=new UnityHostRouteService())
                {
                    var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                    for(int i=0;i<200&&session.AppliedResults<2;i++){host.Service(binding,8);session.ApplyResults();}
                }
                Assert.AreEqual(2,session.AppliedResults);
                var route=Route(session);
                Assert.True(Budget(session).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope)
                {
                    var reservation=scope.OwnedReservation();
                    var frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,reservation);
                    NavLocalRepairWorkspace scratch=null;
                    try
                    {
                        frame.Observe();
                        int row=frame.Find(2);
                        var actor=frame.After[row];
                        var peer=frame.After[frame.Find(1)];
                        var state=NavRecastIntervals.Initial(route);
                        frame.BindRecast(row,route,state);
                        var goal=actor.GoalLocation.Value;
                        Assert.Greater(actor.route.Length,6);
                        // Close the approach to the choke without closing its far
                        // endpoint. Every current local prefix stays on this bank.
                        var original=frame.Working.Geometry;
                        var blockers=original.Obstacles.Append(new NavObstacle(15,10,18,61)).ToArray();
                        var blocked=new NavGeometry(original.HalfExtent,blockers,original.Revision+1);
                        frame.Working.BindGeometryNoThrow(blocked);
                        peer.Moving=false;
                        Assert.True(blocked.IsFree(actor.Position,actor.Radius));
                        Assert.True(blocked.IsFree(goal.Position,actor.Radius));
                        Assert.True(actor.route.Zip(actor.route.Skip(1),(a,b)=>blocked.SegmentFree(a,b,actor.Radius)).Any(clear=>!clear));
                        const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                        typeof(NavCrowd).GetMethod("BuildIndex",hidden).Invoke(frame.Working,null);
                        var repair=typeof(NavCrowd).GetMethod("CommitDetour",hidden);
                        var header=new NavUnitSaveState();actor.CopyCaptureHeaderTo(header);
                        long checkedTraces=NavRecastIntervals.TraceObservationsForTests;
                        var timer=System.Diagnostics.Stopwatch.StartNew();
                        object[] args={actor,actor.Goal,scratch};
                        bool installed;
                        try{installed=(bool)repair.Invoke(frame.Working,args);}
                        finally{scratch=(NavLocalRepairWorkspace)args[2];}
                        long traces=NavRecastIntervals.TraceObservationsForTests-checkedTraces;
                        TestContext.WriteLine($"CLOSED_FIXED_TAIL installed={installed} traces={traces} elapsed_ms={timer.Elapsed.TotalMilliseconds:R} edges={frame.Working.DetourEdgeQueriesForTests} waypoints={actor.route.Length}");
                        Assert.False(installed);
                        Assert.True(actor.SameCaptureHeader(header));
                        Assert.Less(traces,128,"A known closed fixed chord must stop repeated full-tail certificates.");
                    }
                    finally{scratch?.ReleaseOwner();frame.Release();}
                    scope.Discard();
                }
            }
            finally
            {
                session.ReleaseCommittedHostRoot();
                session.DestroyNavigationAuthority();
                Assert.Zero(Budget(session).PhysicalBytes);
                Assert.Zero(Budget(session).HeldBytes);
                Assert.Zero(Budget(session).ActiveScopes);
            }
        }
        [Test] public void RepairReuseCapabilityRequiresTheOwnedPinnedGlobalOfItsWorkingHeader()
        {
            var source=Ready();try{var route=Route(source);Assert.True(Budget(source).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(source.Crowd,source.Crowd.Units.Count,r);
                try{frame.Observe();int row=frame.Find(2);var actor=frame.After[row];frame.BindRecast(row,route,NavRecastIntervals.Initial(route));Assert.IsNull(actor.StorageOwner,"Working values must not own the source actor");Assert.IsNotNull(frame.Globals[row]);
                    Assert.False(frame.Working.CanReuseRepairCertificate(row,actor,route));frame.AllowRepairCertificateReuse=true;Assert.True(frame.Working.CanReuseRepairCertificate(row,actor,route),"The actual pinned global must enable the positive production path");
                    var global=actor.route;try{actor.route=(NavPoint[])global.Clone();Assert.False(frame.Working.CanReuseRepairCertificate(row,actor,route),"An unowned equal-valued clone cannot reuse a certificate");}finally{actor.route=global;}
                    var peer=frame.After[frame.Find(1)];
                    var replacement=NavRetainedArray<NavPoint>.Copy(peer.route,r.TryReserve);
                    try{
                        peer.BindRouteBackingNoThrow(replacement,null);
                        frame.ReplacedRoute(peer);
                        Assert.True(frame.Working.CanReuseRepairCertificate(row,actor,route),"A projected replacement for another actor leaves this pinned array eligible.");
                        actor.BindRouteBackingNoThrow(replacement,null);
                        frame.ReplacedRoute(actor);
                        Assert.False(frame.Working.CanReuseRepairCertificate(row,actor,route),"The receiver whose motor changed must qualify its new backing.");
                    }finally{replacement.Release();}
                }finally{frame.Release();}scope.Discard();}
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();Assert.Zero(Budget(source).PhysicalBytes);Assert.Zero(Budget(source).HeldBytes);Assert.Zero(Budget(source).ActiveScopes);}
        }
        [Test] public void InvalidLocalBankEdgeRetiresOnlyWithOwnedGuideAndNoProgressCredit()
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=profile.AuthoredMap;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);var session=new NavigationSession(801,geometry,profile.Navigation,null,map){UsesRecastGroundRoutes=true};
            byte[] Bytes(){using var state=session.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                session.ConfigureMarch(4,profile.Revision);session.Crowd.Add(1,new NavPoint(26.8,42),.58,7,7.5,canYield:true);session.Crowd.Add(2,new NavPoint(26.8,45.6),.58,7,7.5,canYield:true);
                Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(-26.8,15),new NavPoint(-26.8,18.6)}));using(var host=new UnityHostRouteService()){var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);for(int i=0;i<200&&session.AppliedResults<2;i++){host.Service(binding,8);session.ApplyResults();}}Assert.AreEqual(2,session.AppliedResults);var source=Bytes();var route=Route(session);
                Assert.True(Budget(session).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,r);
                    try{frame.Observe();int row=frame.Find(2);var actor=frame.After[row];var peer=frame.After[frame.Find(1)];peer.Moving=false;peer.Outcome=NavigationOutcome.Stopped;
                        int entry=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal&&route.Legs[i].To.SurfaceId=="bridge/north"),exit=Enumerable.Range(entry+1,route.Legs.Count-entry-1).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);
                        Assert.True(RecastFormationGuide.TryLayout(route,route.Legs[entry].PortalId,route.Legs[exit].PortalId,profile.Navigation.ArrivalSlotSpacing,r,out var layout,actor.Speed*profile.Navigation.CrowdLookaheadSeconds));int lane=layout.Lanes-1;var at=actor.Location;var state=NavRecastIntervals.Initial(route);
                        foreach(var target in new[]{layout.Point(-layout.Radius,lane),layout.Point(layout.Length+layout.Radius,lane)}){Assert.True(NavRecastIntervals.Advance(route,state,at,target,r,out var receipt,frame.Working,actor.Id));at=new NavLocation(receipt.To,receipt.ToSurface);state=receipt.After;}
                        actor.Position=at.Position;actor.Location=at;actor.routeIndex=route.Legs.Take(exit+1).Count(l=>l.Kind==NavTypedLegKind.Surface);actor.Heading=Math.Atan2(layout.Direction.Z,layout.Direction.X);actor.noProgressTicks=27;actor.blockedSeconds=4d/30;frame.BindRecast(row,route,state);
                        var hint=RecastFormationGuide.RecoveringApproach(frame,row,route,entry,layout,lane,actor.Speed,1d/30);frame.Formation[row]=hint;Assert.AreNotEqual(0,hint.Flags&NavFormationFrameHint.Guide);
                        var stale=new NavPoint(actor.Position.X+2*actor.Radius*layout.Normal.X,actor.Position.Z+2*actor.Radius*layout.Normal.Z);Assert.False(RecastFormationMotion.DirectionalAllows(hint,route,actor.Position,stale));
                        void Install(NavPoint next){var local=NavRetainedArray<NavPoint>.Copy(new[]{actor.Position,next},r.TryReserve);frame.NewLocals[row]?.Release();frame.NewLocals[row]=local;actor.localRoute=local.ReadOnlyBacking;actor.localIndex=1;}
                        Install(hint.Target);var valid=actor.localRoute;var capped=hint;capped.Flags|=NavFormationFrameHint.Bound;capped.ForwardLimit=layout.Station(actor.Position)-layout.Pitch;frame.Formation[row]=capped;Assert.False(frame.Working.TryRetireInvalidRecastLocal(row),"A future corner beyond the temporary row cap stays installed");Assert.AreSame(valid,actor.localRoute);
                        frame.Formation[row]=hint;Install(stale);var retained=actor.localRoute;var peerLocation=peer.Location;bool held=peer.Held;peer.Position=actor.Goal;peer.Location=actor.GoalLocation.Value;peer.Held=true;
                        try{Assert.False(frame.Working.TryRetireInvalidRecastLocal(row),"A stale corner does not waive the replacement's full live HOLD certificate");Assert.AreSame(retained,actor.localRoute);Assert.AreEqual(1,actor.localIndex);}finally{peer.Position=peerLocation.Position;peer.Location=peerLocation;peer.Held=held;}
                        int global=actor.routeIndex;var goal=actor.GoalLocation;var position=actor.Position;double heading=actor.Heading;long request=actor.InstalledRequest;
                        Assert.True(frame.Working.TryRetireInvalidRecastLocal(row));Assert.Zero(actor.localRoute.Length);Assert.Zero(actor.localIndex);Assert.IsNotNull(frame.NewLocals[row]);Assert.Zero(frame.NewLocals[row].Count);Assert.AreEqual(global,actor.routeIndex);Assert.AreEqual(27,actor.noProgressTicks);Assert.AreEqual(4d/30,actor.blockedSeconds);Assert.AreEqual(position,actor.Position);Assert.AreEqual(heading,actor.Heading);Assert.AreEqual(goal,actor.GoalLocation);Assert.AreEqual(request,actor.InstalledRequest);
                        // Repeating through normal preparation must not leave a
                        // yielding actor with an old index into the retired array.
                        Install(stale);frame.Compute(1d/30);Assert.Zero(frame.Motion[row].OwnLocalIndex);Assert.Zero(actor.localRoute.Length);Assert.AreEqual(goal,actor.GoalLocation);Assert.AreEqual(request,actor.InstalledRequest);CollectionAssert.AreEqual(source,Bytes());
                    }finally{frame.Release();}scope.Discard();}
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(Budget(session).PhysicalBytes);Assert.Zero(Budget(session).HeldBytes);Assert.Zero(Budget(session).ActiveScopes);}
        }
        [Test]
        public void LocalRepairRetestsTheBlockingPeerAndFindsOtherBlockers()
        {
            var crowd=new NavCrowd(new NavGeometry(96,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            var actor=crowd.Add(1,new NavPoint(0,0),.58,7,7.5);
            for(int id=2;id<=12;id++)
                // Keep these bodies inside the chord's broad bounds so the
                // fixture still measures exact peer predicates and blocker reuse.
                crowd.Add(id,new NavPoint(id*2,1.6),.58,7,7.5);
            var first=crowd.Add(13,new NavPoint(10,0),.58,7,7.5);
            var second=crowd.Add(14,new NavPoint(12,6),.58,7,7.5);
            crowd.Stop(first.Id,true);
            crowd.Stop(second.Id,false);

            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|
                System.Reflection.BindingFlags.NonPublic;
            var ordinary=typeof(NavCrowd).GetMethod("UnitFree",flags,null,
                new[]{typeof(NavUnit),typeof(NavPoint),typeof(NavPoint),typeof(double)},null);
            var hinted=typeof(NavCrowd).GetMethod("UnitFreeWithBlockerHint",flags);

            int beforeOrdinary=crowd.NeighborCandidates;
            var end=new NavPoint(32,0);
            var centers=Enumerable.Range(0,12).Select(i=>new NavPoint(0,i*.01)).ToArray();
            foreach(var from in centers)
                Assert.False((bool)ordinary.Invoke(crowd,new object[]{actor,from,end,0d}));
            int ordinaryQueries=crowd.NeighborCandidates-beforeOrdinary;

            int beforeHint=crowd.NeighborCandidates;
            NavUnit blocker=null;
            bool Clear(NavPoint from,NavPoint to)
            {
                var args=new object[]{actor,from,to,blocker};
                bool clear=(bool)hinted.Invoke(crowd,args);
                blocker=(NavUnit)args[3];
                return clear;
            }
            foreach(var from in centers)
            {
                Assert.False(Clear(from,end));
                Assert.AreSame(first,blocker);
            }
            int hintedQueries=crowd.NeighborCandidates-beforeHint;
            Assert.Less(hintedQueries,ordinaryQueries,"Retesting a known blocker must reduce actual peer predicates.");
            Assert.False(Clear(new NavPoint(0,6),new NavPoint(32,6)));
            Assert.AreSame(second,blocker);
            Assert.True(Clear(new NavPoint(0,12),new NavPoint(32,12)));
            TestContext.WriteLine($"REPAIR_BLOCKER predicates={ordinaryQueries} hinted={hintedQueries}");
        }

        [Test]
        public void LongRepairChordsSkipDistantPeersAndKeepExactDiscClearance()
        {
            var crowd = new NavCrowd(new NavGeometry(96, Array.Empty<NavObstacle>(), 1),
                PlayableProfile.Default.Navigation);
            var actor = crowd.Add(1, new NavPoint(-60, 0), .58, 7, 7.5);
            for (int id = 2; id <= 30; id++)
                crowd.Add(id, new NavPoint(-62 + id * 4, 65), .8, 7, 7.5);
            var first = crowd.Add(31, new NavPoint(-18, 2.7), .72, 7, 7.5);
            crowd.Stop(first.Id, true);
            crowd.Add(32, new NavPoint(19, -3.2), .58, 7, 7.5);

            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;
            var sweep = typeof(NavCrowd).GetMethod("UnitFree", flags, null,
                new[] { typeof(NavUnit), typeof(NavPoint), typeof(NavPoint), typeof(double) }, null);
            int chords = 0, blocked = 0;
            foreach (double offset in new[] { -20d, -4.36, -4.35, -3.2, 0d, 1.4, 2.7, 3.99, 4d, 20d })
            foreach (double tilt in new[] { -.2, 0d, .2 })
            {
                var from = new NavPoint(-62, offset);
                var to = new NavPoint(62, offset + tilt);
                bool expected = !crowd.Units.Any(peer => peer != actor &&
                    NavCrowd.SegmentPointDistanceSquared(peer.Position, from, to) <
                    (actor.Radius + peer.Radius) * (actor.Radius + peer.Radius) - 1e-9);
                Assert.AreEqual(expected, (bool)sweep.Invoke(crowd, new object[] { actor, from, to, 0d }));
                chords++;
                if (!expected) blocked++;
            }
            Assert.Greater(blocked, 0);
            Assert.Less(blocked, chords);
            TestContext.WriteLine($"LONG_REPAIR_CHORDS chords={chords} blocked={blocked} peer_predicates={crowd.NeighborCandidates}");
            Assert.LessOrEqual(crowd.NeighborCandidates, chords * 2,
                "A long chord must not run disc tests against peers across the map.");
        }

        [TestCase(false)][TestCase(true)] public void QualifiedCompleteBankRepairKeepsLegacyEndpointAndLiveHoldDenial(bool observeQueries)
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=profile.AuthoredMap;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);var session=new NavigationSession(801,geometry,profile.Navigation,null,map){UsesRecastGroundRoutes=true};
            var oldObserver=TerrainLocations.IndexedCutCandidateForTests;int callbacks=0;if(observeQueries)TerrainLocations.IndexedCutCandidateForTests=_=>callbacks++;
            try{session.ConfigureMarch(4,profile.Revision);session.Crowd.Add(1,new NavPoint(26.8,42),.58,7,7.5,canYield:true);session.Crowd.Add(2,new NavPoint(26.8,45.6),.58,7,7.5,canYield:true);Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(-26.8,15),new NavPoint(-26.8,18.6)}));
                using(var host=new UnityHostRouteService()){var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);for(int i=0;i<200&&session.AppliedResults<2;i++){host.Service(binding,8);session.ApplyResults();}}Assert.AreEqual(2,session.AppliedResults);var route=Route(session);byte[] SourceBytes(){using(var capture=session.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,capture));}var source=SourceBytes();
                Assert.True(Budget(session).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,r);NavLocalRepairWorkspace scratch=null;
                    try{frame.Observe();int row=frame.Find(2);var actor=frame.After[row];var peer=frame.After[frame.Find(1)];var state=NavRecastIntervals.Initial(route);var at=actor.Location;
                        for(int i=0;i<actor.route.Length-2;i++){Assert.True(NavRecastIntervals.Advance(route,state,at,actor.route[i],r,out var moved,frame.Working,actor.Id),"Accepted route setup "+i);at=new NavLocation(moved.To,moved.ToSurface);state=moved.After;}
                        var center=route.RecastPlan.Spine.FanCenter;Assert.True(NavRecastIntervals.Advance(route,state,at,center,r,out var terminal,frame.Working,actor.Id));at=new NavLocation(terminal.To,terminal.ToSurface);state=terminal.After;Assert.AreEqual(2,state.Phase);actor.Position=at.Position;actor.Location=at;actor.routeIndex=actor.route.Length-1;frame.BindRecast(row,route,state);
                        int entry=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal&&route.Legs[i].To.SurfaceId=="bridge/north"),exit=Enumerable.Range(entry+1,route.Legs.Count-entry-1).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);Assert.True(RecastFormationGuide.TryLayout(route,route.Legs[entry].PortalId,route.Legs[exit].PortalId,profile.Navigation.ArrivalSlotSpacing,r,out var layout,actor.Speed*profile.Navigation.CrowdLookaheadSeconds));var hint=new NavFormationFrameHint{Flags=NavFormationFrameHint.Directional,TargetLeg=entry,DirectionalBankReach=layout.BankReach};frame.Formation[row]=hint;
                        double dx=actor.Goal.X-at.Position.X,dz=actor.Goal.Z-at.Position.Z,length=Math.Sqrt(dx*dx+dz*dz);Assert.Greater(length,actor.Radius+peer.Radius);var direction=new NavPoint(dx/length,dz/length);double side=(actor.Radius+peer.Radius)*.8;
                        peer.Position=new NavPoint(at.Position.X+dx/2-direction.Z*side,at.Position.Z+dz/2+direction.X*side);Assert.True(map.TryLocate(peer.Position,peer.Radius,out var peerAt));peer.Location=peerAt;peer.Moving=false;peer.Outcome=NavigationOutcome.Stopped;peer.Held=true;Assert.Greater(Math.Sqrt(Math.Pow(peer.Position.X-at.Position.X,2)+Math.Pow(peer.Position.Z-at.Position.Z,2)),actor.Radius+peer.Radius);Assert.Greater(Math.Sqrt(Math.Pow(peer.Position.X-actor.Goal.X,2)+Math.Pow(peer.Position.Z-actor.Goal.Z,2)),actor.Radius+peer.Radius);
                        typeof(NavCrowd).GetMethod("BuildIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(frame.Working,null);var method=typeof(NavCrowd).GetMethod("CommitDetour",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);NavPoint[] expected=null;int selected=-1;
                        var goal=actor.GoalLocation;var request=actor.InstalledRequest;var global=actor.route;var position=actor.Position;
                        int originalEdges=0,optimizedEdges=0,originalNeighbors=0,optimizedNeighbors=0;
                        foreach(bool original in new[]{true,false})foreach(bool qualified in new[]{false,true}){
                            frame.NewLocals[row]?.Release();frame.NewLocals[row]=null;actor.localRoute=Array.Empty<NavPoint>();actor.localIndex=0;actor.routeIndex=actor.route.Length-1;actor.noProgressTicks=27;actor.blockedSeconds=4d/30;
                            frame.AllowRepairCertificateReuse=qualified;Assert.AreEqual(qualified&&!observeQueries,frame.Working.CanReuseRepairCertificate(row,actor,route));frame.Working.OriginalDetourEdgeOrderForTests=original;
                            int edges=frame.Working.DetourEdgeQueriesForTests,neighbors=frame.Working.NeighborCandidates;object[] args={actor,actor.Goal,scratch};bool installed;
                            try{installed=(bool)method.Invoke(frame.Working,args);}finally{scratch=(NavLocalRepairWorkspace)args[2];}
                            if(original){originalEdges+=frame.Working.DetourEdgeQueriesForTests-edges;originalNeighbors+=frame.Working.NeighborCandidates-neighbors;}else{optimizedEdges+=frame.Working.DetourEdgeQueriesForTests-edges;optimizedNeighbors+=frame.Working.NeighborCandidates-neighbors;}
                            Assert.True(installed);Assert.Greater(actor.localRoute.Length,0);Assert.True(RecastFormationMotion.DirectionalTailAllows(hint,route,actor.localRoute.Last(),actor.route,actor.routeIndex,actor.Goal));Assert.True(NavRecastIntervals.TryContinuationRejoin(route,state,at,actor.localRoute,0,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out int same,frame.Working,actor.Id,true));Assert.AreEqual(actor.routeIndex,same);Assert.AreEqual(27,actor.noProgressTicks);Assert.AreEqual(4d/30,actor.blockedSeconds);
                            Assert.AreEqual(goal,actor.GoalLocation);Assert.AreEqual(request,actor.InstalledRequest);Assert.AreSame(global,actor.route);Assert.AreEqual(position,actor.Position);Assert.Zero(actor.localIndex);Assert.AreEqual(state,frame.RecastStates[row]);CollectionAssert.AreEqual(source,SourceBytes());
                            if(expected==null){expected=actor.localRoute.ToArray();selected=actor.routeIndex;}else{CollectionAssert.AreEqual(expected,actor.localRoute);Assert.AreEqual(selected,actor.routeIndex);}
                        }
                        if(observeQueries){Assert.Greater(callbacks,0);Assert.AreEqual(originalEdges,optimizedEdges,"Observed queries keep the original call sequence");Assert.AreEqual(originalNeighbors,optimizedNeighbors);}else Assert.LessOrEqual(optimizedEdges,originalEdges,"A successful early connector may terminate before any dominated node");Assert.LessOrEqual(optimizedNeighbors,originalNeighbors,"No invented neighbour credit");
                        TestContext.WriteLine("detour edges "+originalEdges+" -> "+optimizedEdges+", actual peer predicates "+originalNeighbors+" -> "+optimizedNeighbors);
                        foreach(bool original in new[]{true,false}){
                            frame.Working.OriginalDetourEdgeOrderForTests=original;frame.NewLocals[row]?.Release();frame.NewLocals[row]=null;actor.localRoute=Array.Empty<NavPoint>();actor.localIndex=0;actor.routeIndex=selected;
                            peer.Position=actor.Goal;peer.Location=actor.GoalLocation.Value;typeof(NavCrowd).GetMethod("BuildIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(frame.Working,null);
                            int edges=frame.Working.DetourEdgeQueriesForTests,neighbors=frame.Working.NeighborCandidates;object[] blocked={actor,actor.Goal,scratch};try{Assert.False((bool)method.Invoke(frame.Working,blocked),"Both searches must retain full live HOLD denial");}finally{scratch=(NavLocalRepairWorkspace)blocked[2];}
                            if(original){originalEdges+=frame.Working.DetourEdgeQueriesForTests-edges;originalNeighbors+=frame.Working.NeighborCandidates-neighbors;}else{optimizedEdges+=frame.Working.DetourEdgeQueriesForTests-edges;optimizedNeighbors+=frame.Working.NeighborCandidates-neighbors;}
                            Assert.Zero(actor.localRoute.Length);Assert.AreEqual(27,actor.noProgressTicks);Assert.AreEqual(selected,actor.routeIndex);Assert.AreEqual(goal,actor.GoalLocation);Assert.AreEqual(state,frame.RecastStates[row]);CollectionAssert.AreEqual(source,SourceBytes());
                        }
                        if(observeQueries){Assert.AreEqual(originalEdges,optimizedEdges);Assert.AreEqual(originalNeighbors,optimizedNeighbors);}else{Assert.Less(optimizedEdges,originalEdges,"The exhausted HOLD-denied search must reject dominated nodes before clearance");Assert.Less(optimizedNeighbors,originalNeighbors,"Count only peer predicates actually performed");}
                        TestContext.WriteLine("success+denial detour edges "+originalEdges+" -> "+optimizedEdges+", actual peer predicates "+originalNeighbors+" -> "+optimizedNeighbors);
                    }finally{scratch?.ReleaseOwner();frame.Release();}scope.Discard();}
            }finally{TerrainLocations.IndexedCutCandidateForTests=oldObserver;session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(Budget(session).PhysicalBytes);Assert.Zero(Budget(session).HeldBytes);Assert.Zero(Budget(session).ActiveScopes);}
        }
        [TestCase(false)][TestCase(true)]
        public void ReplacementRoutesRetireFrozenRolesWithoutChangingSurvivorMapping(bool replaceAll)
        {
            var source=Ready();try{
                var catalog=(NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(source);
                var original=((NavigationLiveGroup)catalog.OwnerFor(source.GroupFor(1).GroupId)).ImmutableVersion;
                Assert.True(Budget(source).TryOpenScope(NavigationPreparationKind.Projection,801,1,out var scope));
                using(scope){var r=scope.OwnedReservation();
                    var frame=NavPreparedCrowdStep.Create(source.Crowd,source.Crowd.Units.Count,r);
                    var membership=NavigationCommandSlots<int,long>.CreateOwned(4,r);
                    RecastFormationPlan frozen=null,result=null;NavigationAuthorityGroup group=null;
                    try{frame.Observe();
                        var installed=new NavigationInstalledExecutionOwner[frame.After.Length];
                        NavigationInstalledExecutionOwner Owner(int id)=>(NavigationInstalledExecutionOwner)source.Crowd.Units.Single(u=>u.Id==id).StorageOwner.InstalledExecution;
                        var routes=original.Members.Select(v=>Owner(((NavigationAuthorityMember)v).Data.Entity).Corridor.TypedRoute).ToArray();
                        var paths=routes.Select(route=>route.RecastPlan.Spine).Distinct().ToArray();
                        int first=Enumerable.Range(0,routes[0].Legs.Count).First(i=>routes[0].Legs[i].Kind==NavTypedLegKind.Portal);
                        int last=Enumerable.Range(first+1,routes[0].Legs.Count-first-1).First(i=>routes[0].Legs[i].Kind==NavTypedLegKind.Portal);
                        string entry=routes[0].Legs[first].PortalId,exit=routes[0].Legs[last].PortalId;
                        Assert.True(RecastFormationGuide.TryLayout(routes[0],entry,exit,source.Crowd.Profile.ArrivalSlotSpacing,r,out var layout));
                        var entries=original.Members.Select((v,i)=>{var m=((NavigationAuthorityMember)v).Data;int row=frame.Find(m.Entity);
                            membership.Add(m.Entity,original.Data.GroupId);installed[row]=Owner(m.Entity);
                            frame.BindRecast(row,routes[i],NavRecastIntervals.Initial(routes[i]));
                            return new RecastFormationEntry(m.Entity,m.Slot,Array.IndexOf(paths,routes[i].RecastPlan.Spine),i,i%layout.Lanes,RecastPassageStage.Admitted,m.Incarnation,m.OrderRevision,m.MobilityRevision,routes[i].Subscription.RequestSequence+(replaceAll||i==0?100:0));}).ToArray();
                        var d=original.Data;
                        frozen=RecastFormationPlan.Create(d.WorldGeneration,d.GroupId,d.OrderRevision,d.CommandSequence,d.MembershipRevision,layout.Lanes,RecastFormationPhase.Feeding,0,false,entry,exit,entries,paths,r);
                        group=NavigationAuthorityGroup.CopyWithFormationOwned(original,frozen,r);
                        result=RecastFormationGuide.Prepare(frame,group,installed,new bool[frame.After.Length],membership,1d/30,r,out bool clear);
                        Assert.NotNull(result);Assert.False(clear);Assert.AreEqual(frozen.Seal,result.Seal);
                        for(int i=0;i<entries.Length;i++){
                            var before=entries[i];var after=result.Entries[i];
                            Assert.AreEqual(replaceAll||i==0?RecastPassageStage.Tombstone:RecastPassageStage.Admitted,after.Stage);
                            Assert.AreEqual(before.Request,after.Request);Assert.AreEqual(before.EntryOrdinal,after.EntryOrdinal);Assert.AreEqual(before.Lane,after.Lane);Assert.AreEqual(before.SpineIndex,after.SpineIndex);
                            Assert.AreEqual(routes[i].Subscription.RequestSequence,installed[frame.Find(before.Entity)].Corridor.TypedRoute.Subscription.RequestSequence);
                        }
                        Assert.True(frozen.Entries.All(e=>e.Stage==RecastPassageStage.Admitted),"Detached preparation cannot mutate the source");
                    }finally{result?.Release();group?.ReleaseStorage();frozen?.Release();membership.ReleaseStorage();frame.Release();}
                }
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();Assert.Zero(Budget(source).PhysicalBytes);Assert.Zero(Budget(source).HeldBytes);Assert.Zero(Budget(source).ActiveScopes);}
        }
        [TestCase(false)][TestCase(true)]
        public void CollectingFormationFreezesOnlyAcceptedPassageRoles(bool held)
        {
            var source=Ready(threeMembers:true);
            try{
                if(!held)source.SetFormationContact(3,true,1);
                var catalog=(NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(source);
                var original=((NavigationLiveGroup)catalog.OwnerFor(source.GroupFor(1).GroupId)).ImmutableVersion;
                Assert.True(Budget(source).TryOpenScope(NavigationPreparationKind.Projection,801,1,out var scope));
                using(scope){
                    var r=scope.OwnedReservation();
                    var frame=NavPreparedCrowdStep.Create(source.Crowd,source.Crowd.Units.Count,r);
                    var membership=NavigationCommandSlots<int,long>.CreateOwned(4,r);
                    RecastFormationPlan collecting=null,frozen=null,resumed=null;
                    NavigationAuthorityGroup group=null,frozenGroup=null;
                    try{
                        frame.Observe();
                        var installed=new NavigationInstalledExecutionOwner[frame.After.Length];
                        foreach(var unit in source.Crowd.Units){
                            int row=frame.Find(unit.Id);
                            membership.Add(unit.Id,original.Data.GroupId);
                            installed[row]=(NavigationInstalledExecutionOwner)unit.StorageOwner.InstalledExecution;
                            var route=installed[row].Corridor.TypedRoute;
                            frame.BindRecast(row,route,NavRecastIntervals.Initial(route));
                        }
                        int excluded=frame.Find(3);
                        frame.After[excluded].Held=held;
                        var detached=new bool[frame.After.Length];
                        collecting=RecastFormationPlan.Collect(original.Data,original.Members,r);
                        group=NavigationAuthorityGroup.CopyWithFormationOwned(original,collecting,r);
                        frozen=RecastFormationGuide.Prepare(frame,group,installed,detached,membership,1d/30,r,out bool clear);
                        Assert.False(clear);
                        Assert.NotNull(frozen);
                        Assert.AreEqual(RecastFormationPhase.Feeding,frozen.Phase);
                        var skipped=frozen.Entries.Single(e=>e.Entity==3);
                        Assert.AreEqual(RecastPassageStage.Tombstone,skipped.Stage);
                        Assert.AreEqual(-1,skipped.SpineIndex);
                        Assert.AreEqual(-1,skipped.EntryOrdinal);
                        Assert.AreEqual(0,skipped.Request);
                        Assert.Zero(frame.Formation[excluded].Flags);
                        CollectionAssert.AreEquivalent(new[]{0,1},frozen.Entries.Where(e=>e.Entity!=3).Select(e=>e.EntryOrdinal));
                        Assert.True(collecting.Entries.All(e=>e.Stage==RecastPassageStage.Staging));

                        // Leaving HOLD/contact must not enroll an unassigned role in an already frozen plan.
                        if(!held){
                            source.SetFormationContact(3,false,2);
                            original=((NavigationLiveGroup)catalog.OwnerFor(source.GroupFor(1).GroupId)).ImmutableVersion;
                        }
                        frame.After[excluded].Held=false;
                        frozenGroup=NavigationAuthorityGroup.CopyWithFormationOwned(original,frozen,r);
                        resumed=RecastFormationGuide.Prepare(frame,frozenGroup,installed,detached,membership,1d/30,r,out clear);
                        Assert.False(clear);
                        var next=resumed??frozen;
                        Assert.AreEqual(frozen.Seal,next.Seal);
                        Assert.AreEqual(RecastPassageStage.Tombstone,next.Entries.Single(e=>e.Entity==3).Stage);
                        Assert.Zero(frame.Formation[excluded].Flags);
                    }finally{
                        resumed?.Release();frozenGroup?.ReleaseStorage();frozen?.Release();
                        group?.ReleaseStorage();collecting?.Release();membership.ReleaseStorage();frame.Release();
                    }
                }
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();Assert.Zero(Budget(source).PhysicalBytes);Assert.Zero(Budget(source).HeldBytes);Assert.Zero(Budget(source).ActiveScopes);}
        }
        [Test] public void RouteReplacementDropsOnlyRequestLocalRecastProgress()
        {
            var member=new GroupMemberState{Entity=2,ProgressVersion=2,ProgressRequest=71,Progress=12,ProgressOffset=4,Recast=new RecastMemberProgress(71,RecastMovementPhase.Shared,2,3,4)};
            Assert.DoesNotThrow(()=>new NavigationAuthorityMember.Values(member).Validate());
            NavigationSession.ResetRouteProgress(member);
            Assert.Zero(member.ProgressRequest);Assert.AreEqual(default(RecastMemberProgress),member.Recast);
            Assert.AreEqual(12,member.Progress);Assert.AreEqual(12,member.ProgressOffset);
            Assert.DoesNotThrow(()=>new NavigationAuthorityMember.Values(member).Validate());
            // The validator must still reject the old mismatched sidecar.
            member.Recast=new RecastMemberProgress(71,RecastMovementPhase.Shared,2,3,4);
            Assert.Throws<ArgumentException>(()=>new NavigationAuthorityMember.Values(member).Validate());
        }
        private static NavigationSession Ready(bool threeMembers=false)
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=profile.AuthoredMap;
            var g=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var s=new NavigationSession(801,g,profile.Navigation,null,map){UsesRecastGroundRoutes=true};
            try{s.ConfigureMarch(4,profile.Revision);s.Crowd.Add(1,new NavPoint(-27.25,42),.72,4,4.5,canYield:true);s.Crowd.Add(2,new NavPoint(-27.25,48),.72,4,4.5,canYield:true);
                if(threeMembers)s.Crowd.Add(3,new NavPoint(-27.25,45),.72,4,4.5,canYield:true);
                var ids=threeMembers?new[]{1,2,3}:new[]{1,2};
                var goals=threeMembers?new[]{new NavPoint(-46,46),new NavPoint(-42.4,42.4),new NavPoint(-46,42.4)}:new[]{new NavPoint(-46,46),new NavPoint(-42.4,42.4)};
                Assert.True(s.MoveGroup(ids,goals));
                using(var host=new UnityHostRouteService()){var binding=new PlayableRouteBinding(g,profile,s.Admission,s.RoutePort);for(int i=0;i<200&&s.AppliedResults<ids.Length;i++){host.Service(binding,8);s.ApplyResults();}}
                Assert.AreEqual(ids.Length,s.AppliedResults);return s;
            }catch{s.ReleaseCommittedHostRoot();throw;}
        }
        private static NavigationRetentionBudget Budget(NavigationSession s)=>s.PhysicalHostBudget;
        private static NavTypedRoute Route(NavigationSession s)=>((NavigationInstalledExecutionOwner)s.Crowd.Units.Single(u=>u.Id==2).StorageOwner.InstalledExecution).Corridor.TypedRoute;
        private static NavLocation At(double x,double z,string surface)=>new NavLocation(new NavPoint(x,z),surface);
        [TestCase("none")][TestCase("goal")][TestCase("cursor")]
        public void AuthoredRecastCurrentRoundtripPreservesBytesAndRejectsTampering(string tamper)
        {
            var source=Ready();var profile=PlayableProfile.RecastGroupCorridorDefault;var map=profile.AuthoredMap;
            try{
                source.Step(1d/30);byte[] bytes;using(var capture=source.CaptureState())bytes=WorldWire.Pack(w=>WorldWire.Write(w,capture));
                NavigationRetentionBudget loadedBudget=null;
                using(var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState)){
                    if(tamper=="goal")state.Crowd.Units.Single(a=>a.Id==2).Goal=new NavPoint(-40,40);
                    if(tamper=="cursor")foreach(var group in state.Groups){var member=group.Members.SingleOrDefault(m=>m.Entity==2);if(member!=null){var old=member.Recast;member.Recast=new RecastMemberProgress(old.Request,old.Phase,Route(source).Legs.Count,old.Station,old.HighWater);}}
                    var restored=new NavigationSession(801,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),profile.Navigation,null,map,null,null,replayOnlyProjection:true){UsesRecastGroundRoutes=true};
                    try{
                        restored.ConfigureMarch(4,profile.Revision);
                        if(tamper=="none"){
                            restored.RestoreState(state);using var capture=restored.CaptureState();CollectionAssert.AreEqual(bytes,WorldWire.Pack(w=>WorldWire.Write(w,capture)));
                        }else Assert.Throws<ArgumentException>(()=>restored.RestoreState(state));
                    }finally{loadedBudget=restored.PhysicalHostBudget;restored.ReleaseCommittedHostRoot();restored.DestroyNavigationAuthority();}
                }
                if(loadedBudget!=null){Assert.Zero(loadedBudget.PhysicalBytes);Assert.Zero(loadedBudget.HeldBytes);Assert.Zero(loadedBudget.ActiveScopes);}
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();Assert.Zero(Budget(source).PhysicalBytes);Assert.Zero(Budget(source).HeldBytes);Assert.Zero(Budget(source).ActiveScopes);}
        }
        [TestCase(false)][TestCase(true)] public void DirectionalExecutionClipsFullBodyEntryExitAndAllowsOnlyRecoveryFromOutside(bool reverse)
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=profile.AuthoredMap;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var session=new NavigationSession(801,geometry,profile.Navigation,null,map){UsesRecastGroundRoutes=true};
            try{
                session.ConfigureMarch(4,profile.Revision);double x=reverse?26.8:-26.8,goal=reverse?-26.8:30.4;
                session.Crowd.Add(1,new NavPoint(x,42),.58,7,7.5,canYield:true);session.Crowd.Add(2,new NavPoint(x,45.6),.58,7,7.5,canYield:true);
                Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(goal,15),new NavPoint(goal,18.6)}));
                using(var host=new UnityHostRouteService()){var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);for(int i=0;i<200&&session.AppliedResults<2;i++){host.Service(binding,8);session.ApplyResults();}}
                Assert.AreEqual(2,session.AppliedResults);var route=Route(session);
                int first=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal&&route.Legs[i].To.SurfaceId=="bridge/north"),last=Enumerable.Range(first+1,route.Legs.Count-first-1).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){
                    Assert.True(RecastFormationGuide.TryLayout(route,route.Legs[first].PortalId,route.Legs[last].PortalId,profile.Navigation.ArrivalSlotSpacing,scope.OwnedReservation(),out var layout));Assert.True(layout.Directional);Assert.AreEqual(3,layout.Lanes);
                    var hint=new NavFormationFrameHint{Flags=NavFormationFrameHint.Bound|NavFormationFrameHint.Directional,TargetLeg=first,ForwardLimit=layout.Length+10};
                    NavPoint P(double t,double side)=>new NavPoint(layout.Entry.X+layout.Direction.X*t+layout.Normal.X*side,layout.Entry.Z+layout.Direction.Z*t+layout.Normal.Z*side);
                    double right=(layout.Low+(layout.Low+layout.High)/2-layout.Radius)/2,wrong=(layout.Low+layout.High)/2+layout.Radius;
                    var displaced=P(-8,wrong);var align=RecastFormationGuide.Aim(layout,displaced,8,layout.Lanes-1);
                    Assert.Greater(layout.Station(align),layout.Station(displaced),"A bank diagonal may advance while aligning before the core");Assert.Less(layout.Station(align),8,"A far displaced approach is bounded before its line can enter the wrong half");
                    Assert.True(map.TryTraverse(new NavLocation(displaced,route.Legs[first].From.SurfaceId),align,.58,out _),"Alignment uses the actual authored bank");
                    Assert.True(RecastFormationMotion.DirectionalAllows(hint,route,displaced,align));
                    var lanePoint=layout.Point(-8,layout.Lanes-1);
                    var aligned=RecastFormationGuide.Aim(layout,lanePoint,8,layout.Lanes-1);Assert.AreEqual(8,layout.Station(aligned),"A body at its frozen lane retains normal forward guidance");
                    // Near either edge the same legal diagonal already aligns
                    // before core entry: do not switch between transverse and
                    // full lookahead. Keep normal flow everywhere inside too.
                    foreach(int lane in Enumerable.Range(0,layout.Lanes))foreach(bool lowEdge in new[]{false,true}){
                        double edge=lowEdge?layout.Low:(layout.Low+layout.High)/2-layout.Radius,toward=lowEdge?1:-1;
                        foreach(double side in new[]{edge-toward*profile.Navigation.ArrivalTolerance/2,edge+toward*profile.Navigation.ArrivalTolerance/2}){
                            var start=P(-8,side);var aim=RecastFormationGuide.Aim(layout,start,8,lane);
                            Assert.AreEqual(8,layout.Station(aim),"A near-edge crossing must retain a continuous full lookahead");
                            Assert.True(map.TryTraverse(new NavLocation(start,route.Legs[first].From.SurfaceId),aim,.58,out _));
                            Assert.True(RecastFormationMotion.DirectionalAllows(hint,route,start,aim),"The entire guide line must align before original core entry");
                        }
                        var middle=P(-8,(layout.Low+(layout.Low+layout.High)/2-layout.Radius)/2);
                        Assert.AreEqual(8,layout.Station(RecastFormationGuide.Aim(layout,middle,8,lane)),"Already legal bank bodies keep their original full forecast");
                    }
                    Assert.True(RecastFormationMotion.DirectionalAllows(hint,route,displaced,align),"Far diagonal bound preserves full original footprint entry");
                    bool Allows(double a,double x,double b,double y)=>RecastFormationMotion.Allows(hint,route,P(a,x),P(b,y));
                    Assert.True(Allows(1,right,2,right));Assert.False(Allows(1,right,2,wrong),"Local prefix cannot cross into opposing traffic");
                    Assert.False(Allows(-2,wrong,1,wrong),"Entry must align on its bank before any body crosses");Assert.True(Allows(-3,wrong,-2,right));Assert.True(Allows(-2,right,1,right));
                    Assert.True(Allows(1,wrong,2,right),"Displaced starting body can recover");Assert.False(Allows(1,wrong,2,wrong+1));Assert.False(Allows(1,wrong,2,wrong),"Parallel wrong-half travel is not recovery");Assert.True(Allows(2,wrong,1,wrong),"Retreat can return to entry alignment");
                    Assert.False(Allows(layout.Length,right,layout.Length+layout.Radius,wrong),"Center exit does not release the rear footprint");Assert.True(Allows(layout.Length+layout.Radius+1,right,layout.Length+2,wrong));
                    hint.ForwardLimit=1;Assert.False(RecastFormationMotion.Allows(hint,route,P(1,right),P(2,right)));Assert.True(RecastFormationMotion.Allows(hint,route,P(1,right),P(2,wrong),true),"Matched receiver exception remains");
                    hint.Flags=NavFormationFrameHint.Directional;hint.ForwardLimit=0;
                    Assert.True(RecastFormationMotion.Allows(hint,route,P(1,right),P(2,right)),"Recovering has no fake role/frontier cap");Assert.False(RecastFormationMotion.Allows(hint,route,P(1,right),P(2,wrong)),"Local Edge and owner filter must enforce Directional without Bound");
                    Assert.True(RecastFormationMotion.Allows(hint,route,P(layout.Length+layout.Radius+1,right),P(layout.Length+layout.Radius+2,wrong)),"Fully outside bank movement remains free");
                    hint.DirectionalBankReach=7*profile.Navigation.CrowdLookaheadSeconds;
                    Assert.False(Allows(-1,wrong,.5,(wrong+(layout.Low+layout.High)/2)/2),"An initially displaced bank body must align before entering the ORIGINAL core; bank recovery cannot weaken core entry");
                    Assert.False(Allows(layout.Length+layout.Radius+1,right,layout.Length+layout.Radius+2,wrong),"The observed exit-bank crossing cannot enter the opposite bank lane before handoff");
                    Assert.True(Allows(layout.Length+layout.Radius+hint.DirectionalBankReach+1,right,layout.Length+layout.Radius+hint.DirectionalBankReach+2,wrong));
                    Assert.False(Allows(-layout.Radius-hint.DirectionalBankReach-1,wrong,-layout.Radius-hint.DirectionalBankReach+1,wrong),"Entry alignment precedes the certified bank corridor");
                    double bankEntry=-layout.Radius-hint.DirectionalBankReach,bankExit=layout.Length+layout.Radius+hint.DirectionalBankReach;
                    double improving=(wrong+(layout.Low+layout.High)/2-layout.Radius)/2;
                    Assert.True(Allows(bankEntry-layout.Radius,wrong,bankEntry+layout.Radius,improving),"A real displaced bank approach may recover continuously across the EXTRA bank boundary");
                    Assert.False(Allows(bankEntry-layout.Radius,wrong,bankEntry+layout.Radius,wrong+layout.Radius),"Bank entry must not move farther into the wrong half");
                    Assert.False(Allows(bankExit+layout.Radius,wrong,bankExit-layout.Radius,improving),"Cleared outgoing actors cannot reenter the bank in the wrong half");
                    Assert.True(map.TryTraverse(new NavLocation(P(bankEntry-layout.Radius,wrong),route.Legs[first].From.SurfaceId),P(bankEntry+layout.Radius,improving),.58,out _),"Monotonic recovery remains on the actual authored bank");
                    hint.Flags=NavFormationFrameHint.Wait;Assert.False(RecastFormationMotion.Allows(hint,route,P(1,right),P(2,right)));scope.Discard();
                }
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
        }
        [TestCase(true,true)][TestCase(false,true)][TestCase(true,false)][TestCase(false,false)] public void DirectionalBankPartialAlignmentMayMoveAwayFromGoalWithFullTypedSuffix(bool coverEndpoints,bool qualifiedProof)
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=profile.AuthoredMap;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var session=new NavigationSession(801,geometry,profile.Navigation,null,map){UsesRecastGroundRoutes=true};
            try{
                session.ConfigureMarch(4,profile.Revision);session.Crowd.Add(1,new NavPoint(26.8,42),.58,7,7.5,canYield:true);session.Crowd.Add(2,new NavPoint(26.8,45.6),.58,7,7.5,canYield:true);
                Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(-26.8,15),new NavPoint(-26.8,18.6)}));
                using(var host=new UnityHostRouteService()){var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);for(int i=0;i<200&&session.AppliedResults<2;i++){host.Service(binding,8);session.ApplyResults();}}Assert.AreEqual(2,session.AppliedResults);
                var route=Route(session);var original=session.Crowd.Units.Single(a=>a.Id==2);int peerId=101;
                // Occupied accepted endpoints rule out EVERY complete connector;
                // moving, non-held bodies do not invalidate the accepted tail.
                var endpoints=original.route.Distinct().ToArray();if(coverEndpoints)foreach(var point in endpoints){int n=peerId-101;var birth=new NavPoint(16+2*(n%4),34+2*(n/4));Assert.True(map.TryLocate(birth,.58,out var location),"Authored clear bank birth "+n+": "+birth.X+","+birth.Z);session.Crowd.Add(peerId++,birth,.58,7,7.5,location,canYield:false);}
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,r);NavLocalRepairWorkspace scratch=null;
                    try{
                        frame.Observe();int row=frame.Find(2);var actor=frame.After[row];int entry=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal&&route.Legs[i].To.SurfaceId=="bridge/north"),exit=Enumerable.Range(entry+1,route.Legs.Count-entry-1).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);
                        Assert.True(RecastFormationGuide.TryLayout(route,route.Legs[entry].PortalId,route.Legs[exit].PortalId,profile.Navigation.ArrivalSlotSpacing,r,out var layout));Assert.True(layout.Directional);
                        var start=new NavLocation(new NavPoint(layout.Entry.X+3*layout.Radius+frame.Profile.LocalGridCell,33.6),route.Legs[entry].From.SurfaceId);Assert.True(map.IsValidLocation(start,actor.Radius));
                        Assert.True(NavRecastIntervals.Advance(route,NavRecastIntervals.Initial(route),actor.Location,start.Position,r,out var setup,frame.Working,actor.Id));
                        actor.Position=start.Position;actor.Location=start;actor.routeIndex=NavRecastIntervals.FirstTargetSurface(route,setup.After);actor.localRoute=Array.Empty<NavPoint>();actor.localIndex=0;actor.noProgressTicks=10;actor.blockedSeconds=10d/30;
                        frame.BindRecast(row,route,setup.After);frame.RecastReceipts[row]=setup;frame.AllowRepairCertificateReuse=qualifiedProof;Assert.AreEqual(qualifiedProof,frame.Working.CanReuseRepairCertificate(row,actor,route));
                        var target=layout.Point(-layout.Radius,layout.Lanes-1);var hint=new NavFormationFrameHint{Flags=NavFormationFrameHint.Guide|NavFormationFrameHint.Bound|NavFormationFrameHint.Directional,Target=target,TargetLeg=entry,ForwardLimit=layout.Length+10};frame.Formation[row]=hint;
                        foreach(var peer in frame.After.Where(a=>a.Id!=actor.Id)){if(peer.Id>=101){peer.Position=endpoints[peer.Id-101];Assert.True(map.TryLocate(peer.Position,peer.Radius,out var location));peer.Location=location;}peer.Moving=false;peer.Held=false;peer.Outcome=NavigationOutcome.Stopped;Assert.GreaterOrEqual(Math.Pow(peer.Position.X-actor.Position.X,2)+Math.Pow(peer.Position.Z-actor.Position.Z,2),Math.Pow(peer.Radius+actor.Radius,2),"Fixed start must be clear of endpoint cover");}
                        typeof(NavCrowd).GetMethod("BuildIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(frame.Working,null);
                        var repair=typeof(NavCrowd).GetMethod("CommitDetour",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);object[] args={actor,actor.route[actor.routeIndex],scratch};bool installed;try{installed=(bool)repair.Invoke(frame.Working,args);}finally{scratch=(NavLocalRepairWorkspace)args[2];}
                        int smoothed=frame.Working.SmoothedDetourPrefixesForTests;
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="directional-bank-partial",installed,Start=start,Target=target,Goal=actor.Goal,Local=actor.localRoute,actor.routeIndex,smoothed}));
                        Assert.True(installed);Assert.Greater(actor.localRoute.Length,0);
                        if(qualifiedProof&&!coverEndpoints)
                            Assert.Less(smoothed,32,"Only nodes with a possible directional tail should require smoothing.");
                        double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));var end=actor.localRoute.Last();Assert.Less(Distance(end,target),Distance(start.Position,target));Assert.Greater(Distance(end,actor.Goal),Distance(start.Position,actor.Goal),"This fixed bank alignment must exercise the old final-goal-progress rejection");
                        if(!coverEndpoints){
                            // A clear typed motor connection is not a complete
                            // repair when its tail turns through counterflow.
                            // This known same-grid witness must not be skipped
                            // by that false early completion.
                            var witness=new NavPoint(start.Position.X,start.Position.Z+frame.Profile.LocalDetourDistance);
                            Assert.True(geometry.SegmentFree(start.Position,witness,actor.Radius));Assert.True(map.TryTraverse(start,witness,actor.Radius,out _));Assert.True(RecastFormationMotion.Allows(hint,route,start.Position,witness));
                            Assert.True(NavRecastIntervals.TryContinuationRejoin(route,setup.After,start,new[]{witness},0,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out _,frame.Working,actor.Id));
                            bool complete=RecastFormationMotion.DirectionalTailAllows(hint,route,end,actor.route,actor.routeIndex,actor.Goal);
                            Assert.True(complete||Distance(end,target)<=Distance(witness,target)+1e-9,"An unusable directional motor suffix must not stop the strict guide-progress search early");
                        }
                        var before=start;foreach(var point in actor.localRoute){Assert.True(RecastFormationMotion.Allows(hint,route,before.Position,point));Assert.True(map.TryTraverse(before,point,actor.Radius,out before));}
                        Assert.True(NavRecastIntervals.TryContinuationRejoin(route,setup.After,start,actor.localRoute,0,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out int same,frame.Working,actor.Id));Assert.AreEqual(actor.routeIndex,same);Assert.AreEqual(10,actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds);Assert.AreNotEqual(NavigationOutcome.Arrived,actor.Outcome);
                    }finally{scratch?.ReleaseOwner();frame.Release();}scope.Discard();}
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)] public void RecoveringExitUsesActualTypedMotorHandoffAndFinishesOnBankWithoutWrongHalfReentry(bool blockedBank,bool certifiedBank)=>RecoveringBankCase(blockedBank,certifiedBank);
        [Test] public void ActualDenseTerminalFanMustRemainReachableAfterBankHandoff()=>RecoveringBankCase(false,true,true);
        [Test] public void DisplacedFullBodyExitKeepsItsCertifiedBankColumnUntilMotorHandoff()=>RecoveringBankCase(false,true,false,true);
        private void RecoveringBankCase(bool blockedBank,bool certifiedBank,bool denseTerminal=false,bool displacedBank=false)
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=profile.AuthoredMap;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var session=new NavigationSession(801,geometry,profile.Navigation,null,map){UsesRecastGroundRoutes=true};
            byte[] Bytes(){using var state=session.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                session.ConfigureMarch(4,profile.Revision);session.Crowd.Add(1,new NavPoint(26.8,42),.58,7,7.5,canYield:!denseTerminal);session.Crowd.Add(2,denseTerminal?new NavPoint(16,43.8):new NavPoint(26.8,45.6),.58,7,7.5,canYield:!denseTerminal);
                Assert.True(session.MoveGroup(new[]{1,2},denseTerminal?new[]{new NavPoint(-16,7.8),new NavPoint(-16,4.2)}:new[]{new NavPoint(-26.8,15),new NavPoint(-26.8,18.6)}));
                using(var host=new UnityHostRouteService()){var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);for(int i=0;i<200&&session.AppliedResults<2;i++){host.Service(binding,8);session.ApplyResults();}}Assert.AreEqual(2,session.AppliedResults);
                var route=Route(session);var source=Bytes();int publications=session.OwnedMovementPublications;
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,r);
                    try{
                        frame.Observe();int row=frame.Find(2);var actor=frame.After[row];var peer=frame.After[frame.Find(1)];peer.Moving=false;peer.Outcome=NavigationOutcome.Stopped;
                        int entry=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal&&route.Legs[i].To.SurfaceId=="bridge/north"),exit=Enumerable.Range(entry+1,route.Legs.Count-entry-1).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);
                        Assert.True(RecastFormationGuide.TryLayout(route,route.Legs[entry].PortalId,route.Legs[exit].PortalId,profile.Navigation.ArrivalSlotSpacing,r,out var layout,certifiedBank?actor.Speed*profile.Navigation.CrowdLookaheadSeconds:0));Assert.True(layout.Directional);if(certifiedBank)Assert.Greater(layout.BankReach,0,"Full provider trace must certify both actual banks");int lane=layout.Lanes-1;
                        var start=actor.Location;var state=NavRecastIntervals.Initial(route);
                        foreach(var target in new[]{layout.Point(-layout.Radius,lane),layout.Point(layout.Length+layout.Radius,lane)}){Assert.True(NavRecastIntervals.Advance(route,state,start,target,r,out var receipt,frame.Working,actor.Id));start=new NavLocation(receipt.To,receipt.ToSurface);state=receipt.After;}
                        if(displacedBank){var displaced=new NavPoint(start.Position.X+layout.Normal.X*layout.Radius/2,start.Position.Z+layout.Normal.Z*layout.Radius/2);Assert.True(NavRecastIntervals.Advance(route,state,start,displaced,r,out var receipt,frame.Working,actor.Id));start=new NavLocation(receipt.To,receipt.ToSurface);state=receipt.After;}
                        Assert.AreEqual(route.Legs[exit].To.SurfaceId,start.SurfaceId);actor.Position=start.Position;actor.Location=start;actor.Heading=Math.Atan2(layout.Direction.Z,layout.Direction.X);actor.routeIndex=route.Legs.Take(exit+1).Count(v=>v.Kind==NavTypedLegKind.Surface);actor.localRoute=Array.Empty<NavPoint>();actor.localIndex=0;actor.noProgressTicks=10;actor.blockedSeconds=10d/30;frame.BindRecast(row,route,state);
                        int originalIndex=actor.routeIndex;var goal=actor.Goal;var initial=RecastFormationGuide.RecoveringApproach(frame,row,route,entry,layout,lane,actor.Speed,1d/30);
                        Assert.AreEqual(0,initial.Flags&NavFormationFrameHint.Bound);Assert.AreNotEqual(0,initial.Flags&NavFormationFrameHint.Directional);Assert.AreNotEqual(0,initial.Flags&NavFormationFrameHint.Guide,"Fixed old native exit motor must require a qualified bank connector");Assert.AreEqual(originalIndex,actor.routeIndex,"A forecast connector cannot advance a motor cursor");Assert.False(RecastFormationMotion.DirectionalAllows(initial,route,start.Position,actor.route[originalIndex]));Assert.AreEqual(10,actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds);
                        if(displacedBank){Assert.AreEqual(start.Position.Z,initial.Target.Z,"After full-body exit a certified forward bank aim must not pull the displaced body back into the fixed lane point");Assert.Less(initial.Target.X,start.Position.X);Assert.AreNotEqual(layout.Point(layout.Length+layout.Radius+layout.BankReach,lane),initial.Target);}
                        if(denseTerminal)Assert.True(RecastFormationMotion.DirectionalAllows(initial,route,initial.Target,actor.Goal),"Actual16 goal23 must remain outside the bank collar after its certified handoff point");
                        var oldPeer=peer.Location;peer.Position=actor.GoalLocation.Value.Position;peer.Location=actor.GoalLocation.Value;peer.Held=true;
                        try{var denied=RecastFormationGuide.RecoveringApproach(frame,row,route,entry,layout,lane,actor.Speed,1d/30);Assert.AreEqual(0,denied.Flags&NavFormationFrameHint.Guide,"Current HOLD-invalid suffix denies the bank guide too");Assert.AreEqual(originalIndex,actor.routeIndex);Assert.AreEqual(10,actor.noProgressTicks);}finally{peer.Held=false;peer.Position=oldPeer.Position;peer.Location=oldPeer;}
                        if(blockedBank){
                            double along=actor.Radius+2*frame.Profile.LocalGridCell,lateral=actor.Radius+frame.Profile.LocalGridCell;
                            var hold=new NavPoint(start.Position.X+layout.Direction.X*along+layout.Normal.X*lateral,start.Position.Z+layout.Direction.Z*along+layout.Normal.Z*lateral);Assert.True(map.TryLocate(hold,peer.Radius,out var heldLocation));peer.Position=hold;peer.Location=heldLocation;peer.Held=true;
                            Assert.GreaterOrEqual(Math.Pow(hold.X-start.Position.X,2)+Math.Pow(hold.Z-start.Position.Z,2),Math.Pow(peer.Radius+actor.Radius,2));
                            var denied=RecastFormationGuide.RecoveringApproach(frame,row,route,entry,layout,lane,actor.Speed,1d/30);Assert.AreEqual(NavFormationFrameHint.Directional,denied.Flags,"Permanent HOLD blocks only the preferred straight bank connector");
                            double PeerDistance(NavPoint target){double dx=target.X-start.Position.X,dz=target.Z-start.Position.Z,q=dx*dx+dz*dz,t=q==0?0:Math.Max(0,Math.Min(1,((hold.X-start.Position.X)*dx+(hold.Z-start.Position.Z)*dz)/q));return Math.Pow(start.Position.X+t*dx-hold.X,2)+Math.Pow(start.Position.Z+t*dz-hold.Z,2);}
                            for(int n=NavRecastIntervals.FirstTargetSurface(route,state);n<actor.route.Length;n++)Assert.True(!RecastFormationMotion.DirectionalAllows(denied,route,start.Position,actor.route[n])||PeerDistance(actor.route[n])<Math.Pow(peer.Radius+actor.Radius,2),"Every direct accepted target ray must be domain-refused or actually HOLD-blocked: "+n);
                            typeof(NavCrowd).GetMethod("BuildIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(frame.Working,null);
                        }
                        bool translated=false,handoff=false,localCertificate=false;int ticks=0,bankSamples=0;var assigned=actor.GoalLocation;long request=actor.InstalledRequest;
                        for(;ticks<600&&actor.Outcome!=NavigationOutcome.Arrived;ticks++){
                            var before=actor.Location;var prior=frame.RecastStates[row];int index=actor.routeIndex,repairsBefore=frame.Working.RepairCount,npBefore=actor.noProgressTicks;bool local=actor.localIndex<actor.localRoute.Length;double heading=actor.Heading;
                            var hint=RecastFormationGuide.RecoveringApproach(frame,row,route,entry,layout,lane,actor.Speed,1d/30);if(hint.DirectionalBankReach>0&&layout.Station(actor.Position)<layout.Length+layout.Radius+layout.BankReach){bankSamples++;Assert.AreNotEqual(0,hint.Flags&NavFormationFrameHint.Guide,"A certified outgoing bank corridor keeps its guide until actual safe handoff");}frame.Formation[row]=hint;frame.Compute(1d/30);var receipt=frame.RecastReceipts[row];
                            Assert.True(RecastFormationMotion.Allows(hint,route,before.Position,actor.Position));Assert.True(map.TryTraverse(before,actor.Position,actor.Radius,out var actual)&&actual.Equals(actor.Location));Assert.True(geometry.SegmentFree(before.Position,actor.Position,actor.Radius));Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(actor.Heading-heading)),actor.TurnSpeed/30+1e-12);
                            if(blockedBank)Assert.GreaterOrEqual(Math.Pow(actor.Position.X-peer.Position.X,2)+Math.Pow(actor.Position.Z-peer.Position.Z,2),Math.Pow(actor.Radius+peer.Radius,2)-1e-9);
                            if(frame.Working.RepairCount>repairsBefore){Assert.Greater(actor.localRoute.Length,actor.localIndex);Assert.True(NavRecastIntervals.TryContinuationRejoin(route,prior,before,actor.localRoute,actor.localIndex,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out int joined,frame.Working,actor.Id));Assert.AreEqual(actor.routeIndex,joined);Assert.GreaterOrEqual(actor.noProgressTicks,npBefore,"Installing a certified around prefix is not translation credit");localCertificate=true;}
                            if(!local&&actor.localIndex>=actor.localRoute.Length&&actor.routeIndex!=index&&actor.routeIndex<actor.route.Length){Assert.True(NavRecastIntervals.TryContinuationRejoin(route,prior,before,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out int same,frame.Working,actor.Id));Assert.AreEqual(actor.routeIndex,same);Assert.True(RecastFormationMotion.DirectionalAllows(hint,route,before.Position,actor.route[actor.routeIndex]));handoff=true;}
                            if(receipt.Verified)frame.RecastStates[row]=receipt.After;if(!actor.Position.Equals(before.Position)){translated=true;Assert.True(receipt.Verified);Assert.Zero(actor.noProgressTicks);}
                            Assert.AreEqual(goal,actor.Goal);Assert.AreEqual(assigned,actor.GoalLocation);Assert.AreEqual(request,actor.InstalledRequest);
                        }
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="recovering-actual-bank-handoff",blockedBank,certifiedBank,denseTerminal,bankSamples,Start=start,OldTarget=actor.route[originalIndex],InitialFlags=initial.Flags,BankGuide=initial.Target,translated,handoff,localCertificate,ticks,actor.Position,actor.Outcome,actor.routeIndex,frame.Working.RepairCount}));Assert.True(translated);if(certifiedBank&&!blockedBank)Assert.Greater(bankSamples,0);Assert.True(handoff||blockedBank&&localCertificate);if(blockedBank)Assert.True(peer.Held,"The alternative must complete while the preferred connector stays blocked");Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome);Assert.LessOrEqual(Math.Sqrt(Math.Pow(actor.Position.X-goal.X,2)+Math.Pow(actor.Position.Z-goal.Z,2)),profile.Navigation.ArrivalTolerance);CollectionAssert.AreEqual(source,Bytes());Assert.AreEqual(publications,session.OwnedMovementPublications);
                    }finally{frame.Release();}scope.Discard();}
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
        }
        [Test] public void MissedSurfacePointAndDifferentLegalSeamCrossingAdvanceOrderedInterval()
        {
            var s=Ready();try{
                var route=Route(s);var p=route.RecastPlan;int portal=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);
                Assert.AreEqual("bank/west",route.Legs[portal].From.SurfaceId);Assert.AreEqual("ramp/-1/1/x",route.Legs[portal].To.SurfaceId);
                var before=At(-29.49991393925073,45.58051079028784,"bank/west");var after=new NavPoint(-29.51317328835296,45.57291391940347);
                var state=new NavRecastStepState(1,p.OriginLegs+1,1.7,2.2);
                Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope){Assert.True(NavRecastIntervals.Advance(route,state,before,after,scope.OwnedReservation(),out var receipt));
                    Assert.True(receipt.Verified);Assert.AreEqual(1,receipt.PortalCount);Assert.AreEqual(route.Legs[portal].PortalId,receipt.FirstSeam);
                    Assert.AreEqual(1,receipt.After.Phase);Assert.Greater(receipt.After.Cursor,portal);Assert.Greater(receipt.After.Station,p.Spine.Station(portal-p.OriginLegs));Assert.GreaterOrEqual(receipt.After.HighWater,state.HighWater);
                    Assert.False(NavRecastIntervals.CanTarget(route,receipt.After,At(after.X,after.Z,"ramp/-1/1/x"),2,scope.OwnedReservation()),"A consumed physical seam cannot send the actor back to an unvisited bank sample.");
                    scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();}
        }
        [Test] public void ExclusivePortalAllowsEarlierCertifiedSeamsAndRejectsEntryInBothDirections()
        {
            var s=Ready();try{var route=Route(s);int portal=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);int later=Enumerable.Range(portal+1,route.Legs.Count-portal-1).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);
                var from=At(-29.49991393925073,45.58051079028784,"bank/west");var to=new NavPoint(-29.51317328835296,45.57291391940347);var state=new NavRecastStepState(1,route.RecastPlan.OriginLegs+1,1.7,2.2);
                Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();
                    Assert.True(NavRecastIntervals.Advance(route,state,from,to,r,out var earlier,exclusivePortal:later),"Earlier accepted seam remains legal prefix");
                    Assert.False(NavRecastIntervals.Advance(route,state,from,to,r,out _,exclusivePortal:portal),"The actual entry seam is forbidden");
                    var other=new NavLocation(to,earlier.ToSurface);Assert.True(NavRecastIntervals.Advance(route,earlier.After,other,from.Position,r,out _),"Reverse control remains physically accepted");
                    Assert.False(NavRecastIntervals.Advance(route,earlier.After,other,from.Position,r,out _,exclusivePortal:portal),"Reverse crossing of the forbidden seam cannot bypass the qualifier");scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(Budget(s).PhysicalBytes);Assert.Zero(Budget(s).HeldBytes);Assert.Zero(Budget(s).ActiveScopes);}
        }
        [Test] public void EntryIsAnAuthorityConnectedCrossSectionRatherThanCommonPointVisit()
        {
            var s=Ready();try{var route=Route(s);var initial=NavRecastIntervals.Initial(route);var pose=At(-27.6,43.5,"bank/west");
                Assert.Greater(Math.Sqrt(Math.Pow(pose.Position.X+27.25,2)+Math.Pow(pose.Position.Z-42,2)),.15);
                Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){Assert.True(NavRecastIntervals.Advance(route,initial,pose,pose.Position,scope.OwnedReservation(),out var receipt));Assert.AreEqual(1,receipt.After.Phase);scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();}
        }
        [Test] public void WrongLayerWrongSeamAndUnenteredTerminalAreRejected()
        {
            var s=Ready();try{var route=Route(s);var state=NavRecastIntervals.Initial(route);
                Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();
                    Assert.False(NavRecastIntervals.Advance(route,state,At(-27.25,48,"deck/-1/1"),new NavPoint(-27.25,47.9),r,out _));
                    Assert.False(NavRecastIntervals.Advance(route,state,At(-29.49,-45,"bank/west"),new NavPoint(-29.51,-45),r,out _),"Real SW seam is not the accepted next NW seam.");
                    int surfaces=route.Legs.Count(l=>l.Kind==NavTypedLegKind.Surface);
                    Assert.False(NavRecastIntervals.CanTarget(route,state,route.Subscription.Origin,surfaces,r),"A safe goal trace cannot swallow the shared route before physical fan admission.");scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();}
        }
        [TestCase(1)]
        [TestCase(-1)]
        public void IntegratedFanEntryOpensTerminalWithoutASecondMotorWaypoint(int side)
        {
            // Exact east withdrawal trace, and its reflection across X.
            var start=At(side*2.926533212800608,0,"open-ground");
            var entry=At(side*3.406533191318399,.00014360682417100933,"open-ground");
            var moved=At(entry.Position.X,.00014360682417101725,"open-ground");
            var goal=At(side*4.1265331590951195,.00035901694838249735,"open-ground");
            const double radius=.72;
            double dx=moved.Position.X-goal.Position.X,dz=moved.Position.Z-goal.Position.Z;
            Assert.Greater(dx*dx+dz*dz,radius*radius);
            Assert.AreNotEqual(entry,moved);

            var map=new ProvingGroundMap();
            var geometry=new NavGeometry(map.HalfExtent,Array.Empty<NavObstacle>(),map.Revision);
            var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));
            using(scope)
            {
                var r=scope.OwnedReservation();
                var provider=PartitionNavigationProvider.CopyOwned(new PartitionNavigationProvider(map,geometry,""),r);
                var first=NavTypedLeg.CreateOwned(start,entry,.48,NavTypedLegKind.Surface,null,r,provider);
                var last=NavTypedLeg.CreateOwned(entry,goal,.72,NavTypedLegKind.Surface,null,r,provider);
                string binding=new string('a',64);
                var spine=NavRecastSpine.Create(1,1,1,1,radius,binding,binding,binding,provider.Binding,binding,"",1,goal.Position,radius,1,new[]{first,last},r);
                var plan=NavRecastMemberPlan.Create(spine,0,1,0,1,binding,binding,r);
                var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,start,goal,"");
                var route=NavTypedRoute.CopySubscriptionAndCreateOwned(subscription,new[]{first,last},1.2,r,plan);
                try
                {
                    var before=NavRecastIntervals.Initial(route);
                    Assert.False(NavRecastIntervals.CanTarget(route,before,start,1,r));
                    Assert.False(NavRecastIntervals.CanTarget(route,before,At(entry.Position.X-side*1e-10,entry.Position.Z,entry.SurfaceId),1,r));
                    Assert.True(NavRecastIntervals.Advance(route,before,start,moved.Position,r,out var receipt));
                    Assert.AreEqual(2,receipt.After.Phase);
                    Assert.True(NavRecastIntervals.CanTarget(route,receipt.After,moved,1,r));
                }
                finally
                {
                    route.ReleaseOwner();plan.Release();spine.Release();last.ReleaseOwner();first.ReleaseOwner();((INavStorageFacade)provider).StorageRoot.Release();
                }
                scope.Discard();
            }
            Assert.Zero(budget.PhysicalBytes);
            Assert.Zero(budget.HeldBytes);
        }

        [Test] public void PhysicalRejoinRemainsAvailableAfterForecastTargetAheadAndDisplacementOutsideFan()
        {
            var s=Ready();try{var route=Route(s);var p=route.RecastPlan;var fan=p.Spine.Node(p.Exit);double dx=fan.Position.X-p.Spine.FanCenter.X,dz=fan.Position.Z-p.Spine.FanCenter.Z,len=Math.Sqrt(dx*dx+dz*dz);
                var outside=At(fan.Position.X+dx/len,fan.Position.Z+dz/len,fan.SurfaceId);Assert.False(NavRecastIntervals.InTerminalFan(route,outside));
                var state=new NavRecastStepState(1,p.TerminalLeg-1,p.Spine.Station(p.Exit),p.Spine.Station(p.Exit));int forecast=route.Legs.Count(l=>l.Kind==NavTypedLegKind.Surface);
                Assert.Less(NavRecastIntervals.FirstTargetSurface(route,state),forecast);
                Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();Assert.False(NavRecastIntervals.CanTarget(route,state,outside,forecast,r));Assert.True(Enumerable.Range(NavRecastIntervals.FirstTargetSurface(route,state),forecast-NavRecastIntervals.FirstTargetSurface(route,state)).Any(i=>NavRecastIntervals.CanTarget(route,state,outside,i,r)),"Physical interval supplies safe forward fan/shared rejoin despite an earlier forecast endpoint.");scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();}
        }
        [Test] public void CompletedLastSharedLocalRejoinsQualifiedAcceptedSuffixWithoutOccupiedGuide()
        {
            // Controlled semantic state on ONE normal Ready setup, not actor31
            // replay. Direct private derivation only: no Session.Step/Compute.
            ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;
            var s=Ready();
            byte[] Bytes(){using var state=s.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                var route=Route(s);var exitSurface=route.RecastPlan.Spine.Node(route.RecastPlan.Exit).SurfaceId;
                int exit=Enumerable.Range(1,route.Legs.Count-1).Last(i=>route.Legs[i].Kind==NavTypedLegKind.Portal&&route.Legs[i].To.SurfaceId==exitSurface);
                int entry=Enumerable.Range(0,exit).Last(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);
                var last=route.Legs[exit-1];Assert.AreEqual(NavTypedLegKind.Surface,last.Kind);
                int index=route.Legs.Take(exit+1).Count(l=>l.Kind==NavTypedLegKind.Surface);
                var before=Bytes();int publications=s.OwnedMovementPublications;
                Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,r);
                    try{
                        frame.Observe();int row=frame.Find(2);var actor=frame.After[row];
                        var position=new NavPoint((last.From.Position.X+last.To.Position.X)/2,(last.From.Position.Z+last.To.Position.Z)/2);
                        actor.Position=position;actor.Location=new NavLocation(position,last.From.NameReference);actor.routeIndex=index;actor.localRoute=new[]{position};actor.localIndex=1;
                        var state=new NavRecastStepState(1,exit-1,route.RecastPlan.Spine.Station(route.RecastPlan.Exit),route.RecastPlan.Spine.Station(route.RecastPlan.Exit));frame.BindRecast(row,route,state);
                        Assert.Less(index,actor.route.Length);var target=actor.route[index];var next=route.Legs[exit+1];
                        var guide=new NavPoint((next.From.Position.X+next.To.Position.X)/2,(next.From.Position.Z+next.To.Position.Z)/2);
                        Assert.AreNotEqual(target,guide);Assert.True(RecastFormationMotion.Axis(route,entry,out var origin,out _,out var direction,out _));
                        var hint=new NavFormationFrameHint{Flags=NavFormationFrameHint.Guide|NavFormationFrameHint.Bound,TargetLeg=entry,Target=guide,ForwardLimit=RecastFormationMotion.Station(position,origin,direction)+s.Crowd.Profile.ArrivalSlotSpacing};frame.Formation[row]=hint;
                        frame.Motion[row]=new NavMotionBody{Id=actor.Id,Position=position,Target=guide,Active=true,OwnMoving=true,OwnRouteIndex=index,OwnLocalIndex=1,Radius=actor.Radius,Speed=actor.Speed,TurnSpeed=actor.TurnSpeed,Heading=actor.Heading};
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="controlled-completed-lastShared",entry,exit,index,route.RecastPlan.TerminalLeg,Position=position,Target=target,Guide=guide,SourceBytes=before.Length,SessionSteps=0}));
                        Assert.True(NavRecastIntervals.Advance(route,state,actor.Location,guide,r,out var guideReceipt,frame.Working,actor.Id));Assert.AreEqual(exitSurface,guideReceipt.ToSurface);
                        Assert.True(frame.After.Where(a=>a.Id!=actor.Id).All(a=>Math.Pow(a.Position.X-guide.X,2)+Math.Pow(a.Position.Z-guide.Z,2)>=Math.Pow(a.Radius+actor.Radius,2)),"The guide endpoint must actually be free");
                        Assert.True(NavRecastIntervals.CanTarget(route,state,actor.Location,index,r,frame.Working,actor.Id));
                        Assert.True(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,Array.Empty<NavPoint>(),0,actor.route,index,actor.GoalLocation.Value,r,out int same,frame.Working,actor.Id));Assert.AreEqual(index,same);
                        var redirect=typeof(NavCrowd).GetMethod("RedirectOccupiedExitGuides",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                        void Derive()=>redirect.Invoke(frame.Working,new object[]{frame.Motion});
                        var bodies=(NavMotionBody[])frame.Motion.Clone();var hints=(NavFormationFrameHint[])frame.Formation.Clone();var header=new NavUnitSaveState();actor.CopyCaptureHeaderTo(header);var global=actor.route;var local=actor.localRoute;var location=actor.Location;long request=actor.InstalledRequest;
                        Derive();
                        for(int i=0;i<bodies.Length;i++){var expectedBody=bodies[i];var expectedHint=hints[i];if(i==row){expectedBody.Target=target;expectedHint.Target=target;}Assert.AreEqual(expectedBody,frame.Motion[i]);Assert.AreEqual(expectedHint,frame.Formation[i]);}
                        Assert.True(actor.SameCaptureHeader(header));Assert.AreEqual(state,frame.RecastStates[row]);
                        var changedHint=frame.Formation[row];Assert.False(RecastFormationMotion.Allows(changedHint,route,position,new NavPoint(position.X+direction.X*(hint.ForwardLimit-RecastFormationMotion.Station(position,origin,direction)+1),position.Z+direction.Z*(hint.ForwardLimit-RecastFormationMotion.Station(position,origin,direction)+1))),"Accepted aim must keep the original forward bound");
                        foreach(string negative in new[]{"active-local","empty-local","wrong-cursor","wrong-support","wrong-request","invalid-suffix","at-bound","no-guide","policy-wait"}){
                            try{
                                Array.Copy(bodies,frame.Motion,bodies.Length);Array.Copy(hints,frame.Formation,hints.Length);
                                if(negative=="active-local")actor.localIndex=0;if(negative=="empty-local"){actor.localRoute=Array.Empty<NavPoint>();actor.localIndex=0;}
                                if(negative=="wrong-cursor")frame.RecastStates[row]=new NavRecastStepState(1,entry,state.Station,state.HighWater);
                                if(negative=="wrong-support")actor.Location=new NavLocation(position,route.Subscription.Origin.NameReference);
                                if(negative=="wrong-request")actor.InstalledRequest++;
                                if(negative=="invalid-suffix"){actor.route=(NavPoint[])global.Clone();actor.route[actor.route.Length-1]=position;}
                                if(negative=="at-bound")frame.Formation[row].ForwardLimit=RecastFormationMotion.Station(position,origin,direction);
                                if(negative=="no-guide")frame.Formation[row].Flags=NavFormationFrameHint.Bound;
                                if(negative=="policy-wait")frame.Formation[row].Flags|=NavFormationFrameHint.PolicyWait;
                                var unchanged=(NavFormationFrameHint[])frame.Formation.Clone();var prior=new NavUnitSaveState();actor.CopyCaptureHeaderTo(prior);Derive();
                                CollectionAssert.AreEqual(bodies,frame.Motion,negative);CollectionAssert.AreEqual(unchanged,frame.Formation,negative);Assert.True(actor.SameCaptureHeader(prior),negative);
                            }finally{actor.route=global;actor.localRoute=local;actor.localIndex=1;actor.Location=location;actor.InstalledRequest=request;frame.RecastStates[row]=state;}
                        }
                        Array.Copy(bodies,frame.Motion,bodies.Length);Array.Copy(hints,frame.Formation,hints.Length);
                        long physical=Budget(s).PhysicalBytes,held=Budget(s).HeldBytes;int scopes=Budget(s).ActiveScopes,denials=0;long deniedBytes=0;Assert.IsNull(Budget(s).AllocationAdmission);
                        try{
                            Budget(s).AllocationAdmission=bytes=>{denials++;deniedBytes=bytes;return false;};
                            var error=Assert.Throws<System.Reflection.TargetInvocationException>(Derive);Assert.That(error.InnerException,Is.TypeOf<InvalidOperationException>());Assert.AreEqual("Scratch exceeds prepared reservation.",error.InnerException.Message);
                        }finally{Budget(s).AllocationAdmission=null;}
                        Assert.AreEqual(1,denials,"One actual first Shared accepted-proof admission");Assert.Greater(deniedBytes,NavStorageScratch.LeaseBytes);
                        CollectionAssert.AreEqual(bodies,frame.Motion);CollectionAssert.AreEqual(hints,frame.Formation);Assert.True(actor.SameCaptureHeader(header));Assert.AreEqual(state,frame.RecastStates[row]);
                        Assert.AreEqual(physical,Budget(s).PhysicalBytes);Assert.AreEqual(held,Budget(s).HeldBytes);Assert.AreEqual(scopes,Budget(s).ActiveScopes);Assert.AreEqual(publications,s.OwnedMovementPublications);CollectionAssert.AreEqual(before,Bytes());
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="completedShared-derivation-and-denial",Result="PASS",NegativeGuards=9,denials,deniedBytes,FullSourceBytesExact=true,NoPublication=true,SessionSteps=0,PhysicalUnchanged=true,HeldUnchanged=true,ScopesUnchanged=true}));
                    }finally{frame.Release();}scope.Discard();
                }
                CollectionAssert.AreEqual(before,Bytes());Assert.AreEqual(publications,s.OwnedMovementPublications);
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(Budget(s).PhysicalBytes);Assert.Zero(Budget(s).HeldBytes);Assert.Zero(Budget(s).ActiveScopes);Assert.AreEqual(native,RecastNative.sw_recast_allocation_count());Assert.AreEqual(faults,RecastNative.ReleaseFaults);}
        }
        [Test] public void PolicyStoppedAllyCanJoinOnlyAnActuallyBlockedCertifiedYieldCohort()
        {
            var s=Ready();ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;
            byte[] Bytes(){using var state=s.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                var before=Bytes();Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){
                    var frame=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,scope.OwnedReservation());try{
                        frame.Observe();int root=frame.Find(2),peer=frame.Find(1);var actor=frame.After[root];var receiver=frame.After[peer];var route=Route(s);
                        var peerRoute=((NavigationInstalledExecutionOwner)s.Crowd.Units.Single(u=>u.Id==receiver.Id).StorageOwner.InstalledExecution).Corridor.TypedRoute;
                        frame.BindRecast(root,route,NavRecastIntervals.Initial(route));frame.BindRecast(peer,peerRoute,NavRecastIntervals.Initial(peerRoute));
                        actor.Position=new NavPoint(actor.Position.X+2*frame.Profile.ArrivalSlotSpacing,actor.Position.Z);actor.Location=new NavLocation(actor.Position,actor.Location.NameReference);actor.noProgressTicks=10;
                        var target=actor.route[actor.routeIndex];double dx=target.X-actor.Position.X,dz=target.Z-actor.Position.Z,length=Math.Sqrt(dx*dx+dz*dz),heading=Math.Atan2(dz,dx);var d=new NavPoint(dx/length,dz/length);
                        double gap=actor.Radius+receiver.Radius+.01;receiver.Position=new NavPoint(actor.Position.X+d.X*gap,actor.Position.Z+d.Z*gap);receiver.Location=new NavLocation(receiver.Position,actor.Location.NameReference);
                        foreach(var u in frame.After){Assert.True(s.Crowd.Geometry.IsFree(u.Position,u.Radius));Assert.True(PlayableProfile.RecastGroupCorridorDefault.AuthoredMap.IsValidLocation(u.Location,u.Radius));}
                        frame.Motion[root]=new NavMotionBody{Id=actor.Id,Position=actor.Position,Target=target,Heading=heading,Radius=actor.Radius,Speed=actor.Speed,TurnSpeed=actor.TurnSpeed,Active=true,OwnMoving=true,OwnRouteIndex=actor.routeIndex,OwnLocalIndex=actor.localIndex};
                        frame.Motion[peer]=new NavMotionBody{Id=receiver.Id,Position=receiver.Position,Target=receiver.Goal,Heading=heading,Radius=receiver.Radius,Speed=receiver.Speed,TurnSpeed=receiver.TurnSpeed,Active=true,OwnMoving=true,OwnRouteIndex=receiver.routeIndex,OwnLocalIndex=receiver.localIndex};
                        frame.Proposals[root]=new NavMotionProposal{Position=actor.Position,Heading=heading,JointRefused=true};frame.Proposals[peer]=new NavMotionProposal{Position=receiver.Position,Heading=heading};
                        frame.Formation[peer]=new NavFormationFrameHint{Flags=NavFormationFrameHint.PolicyWait};frame.NeighborCounts[root]=frame.NeighborCounts[peer]=1;
                        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                        typeof(NavCrowd).GetMethod("BuildIndex",flags).Invoke(frame.Working,null);
                        var retry=typeof(NavCrowd).GetMethod("RetryPolicyStoppedYield",flags);Assert.IsNotNull(retry);
                        var bodies=(NavMotionBody[])frame.Motion.Clone();var proposals=(NavMotionProposal[])frame.Proposals.Clone();var hints=(NavFormationFrameHint[])frame.Formation.Clone();var headers=frame.After.Select(a=>{var h=new NavUnitSaveState();a.CopyCaptureHeaderTo(h);return h;}).ToArray();int team=receiver.Team;
                        void Reset(){Array.Copy(bodies,frame.Motion,bodies.Length);Array.Copy(proposals,frame.Proposals,proposals.Length);Array.Copy(hints,frame.Formation,hints.Length);frame.RecastRoutes[root]=route;frame.RecastRoutes[peer]=peerRoute;frame.NeighborCounts[root]=frame.NeighborCounts[peer]=1;actor.noProgressTicks=10;actor.Held=receiver.Held=false;receiver.Team=team;for(int i=0;i<frame.YieldLabels.Length;i++)frame.YieldLabels[i]=-1;frame.YieldLabels[peer]=0;}
                        void Retry()=>retry.Invoke(frame.Working,new object[]{1d/30,frame.Motion,frame.Proposals,frame.YieldLabels,frame.YieldQueue,frame.NeighborCounts});
                        foreach(string negative in new[]{"held-peer","held-root","foreign-team","no-policy-refusal","moving-peer","root-wait","root-policy-wait","fresh-root","unknown-q9","no-root-route","no-peer-route","no-root-refusal","terminal-peer"}){
                            Reset();if(negative=="held-peer")receiver.Held=true;if(negative=="held-root")actor.Held=true;if(negative=="foreign-team")receiver.Team=team+1;if(negative=="no-policy-refusal")frame.Formation[peer]=default;
                            if(negative=="moving-peer")frame.Proposals[peer]=new NavMotionProposal{Position=new NavPoint(receiver.Position.X+d.X/100,receiver.Position.Z+d.Z/100),Velocity=new NavPoint(d.X*.3,d.Z*.3),Heading=heading};
                            if(negative=="root-wait")frame.Formation[root].Flags|=NavFormationFrameHint.Wait;if(negative=="root-policy-wait")frame.Formation[root].Flags|=NavFormationFrameHint.PolicyWait;
                            if(negative=="fresh-root")actor.noProgressTicks=0;if(negative=="unknown-q9")frame.NeighborCounts[root]=-1;if(negative=="no-root-route")frame.RecastRoutes[root]=null;if(negative=="no-peer-route")frame.RecastRoutes[peer]=null;
                            if(negative=="no-root-refusal")frame.Proposals[root].JointRefused=false;if(negative=="terminal-peer")frame.Motion[peer].TerminalSeed=true;
                            var expectedBodies=(NavMotionBody[])frame.Motion.Clone();var expectedProposals=(NavMotionProposal[])frame.Proposals.Clone();Retry();CollectionAssert.AreEqual(expectedBodies,frame.Motion,negative);CollectionAssert.AreEqual(expectedProposals,frame.Proposals,negative);
                        }
                        Reset();Retry();
                        Assert.True(frame.Motion[root].Yielding);Assert.True(frame.Motion[peer].Yielding);Assert.Greater(frame.Motion[root].YieldLabel,0);Assert.AreEqual(frame.Motion[root].YieldLabel,frame.Motion[peer].YieldLabel);
                        foreach(int i in new[]{root,peer}){var body=frame.Motion[i];var q=frame.Proposals[i];Assert.AreNotEqual(body.Position,q.Position);Assert.LessOrEqual(NavCrowdMotion.Length(q.Velocity),bodies[i].Speed);Assert.LessOrEqual(Math.Sqrt(Math.Pow(q.Position.X-body.Position.X,2)+Math.Pow(q.Position.Z-body.Position.Z,2)),bodies[i].Speed/30);Assert.True(frame.RecastReceipts[i].Verified);Assert.AreEqual(q.Position,frame.RecastReceipts[i].To);Assert.True(frame.After[i].SameCaptureHeader(headers[i]));}
                        Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(new NavPoint(actor.Position.X-receiver.Position.X,actor.Position.Z-receiver.Position.Z),new NavPoint(frame.Proposals[root].Velocity.X-frame.Proposals[peer].Velocity.X,frame.Proposals[root].Velocity.Z-frame.Proposals[peer].Velocity.Z),1d/30),gap*gap-1e-9);
                        Assert.AreEqual(10,actor.noProgressTicks,"Preparation gives no no-progress credit");
                    }finally{frame.Release();}
                }
                CollectionAssert.AreEqual(before,Bytes(),"Cohort preparation cannot publish or alter the source");Assert.AreEqual(native,RecastNative.sw_recast_allocation_count());Assert.AreEqual(faults,RecastNative.ReleaseFaults);
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(Budget(s).PhysicalBytes);Assert.Zero(Budget(s).HeldBytes);Assert.Zero(Budget(s).ActiveScopes);Assert.Zero(RecastNative.sw_recast_allocation_count());}
        }
        [TestCase("joint")][TestCase("blocked")][TestCase("static")]
        public void PreviouslyStalledAllyWithActualPhysicalRefusalCanJoinCertifiedLateCohort(string refusal)
        {
            var s=Ready();ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;
            byte[] Bytes(){using var capture=s.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,capture));}
            try{
                var before=Bytes();Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){
                    var frame=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,scope.OwnedReservation());try{
                        frame.Observe();int root=frame.Find(2),peer=frame.Find(1);var actor=frame.After[root];var receiver=frame.After[peer];
                        foreach(int row in new[]{root,peer}){var owner=(NavigationInstalledExecutionOwner)frame.Source[row].StorageOwner.InstalledExecution;frame.BindRecast(row,owner.Corridor.TypedRoute,NavRecastIntervals.Initial(owner.Corridor.TypedRoute));}
                        actor.Position=new NavPoint(actor.Position.X+2*frame.Profile.ArrivalSlotSpacing,actor.Position.Z);actor.Location=new NavLocation(actor.Position,actor.Location.NameReference);
                        var target=actor.route[actor.routeIndex];double dx=target.X-actor.Position.X,dz=target.Z-actor.Position.Z,length=Math.Sqrt(dx*dx+dz*dz),heading=Math.Atan2(dz,dx);var direction=new NavPoint(dx/length,dz/length);
                        double gap=actor.Radius+receiver.Radius+.01;receiver.Position=new NavPoint(actor.Position.X+direction.X*gap,actor.Position.Z+direction.Z*gap);receiver.Location=new NavLocation(receiver.Position,actor.Location.NameReference);
                        foreach(int row in new[]{root,peer}){var u=frame.After[row];u.noProgressTicks=10;frame.Motion[row]=new NavMotionBody{Id=u.Id,Position=u.Position,Target=row==root?target:u.Goal,Heading=heading,Radius=u.Radius,Speed=u.Speed,TurnSpeed=u.TurnSpeed,Active=true,OwnMoving=true,OwnRouteIndex=u.routeIndex,OwnLocalIndex=u.localIndex};frame.NeighborCounts[row]=1;}
                        frame.Proposals[root]=new NavMotionProposal{Position=actor.Position,Heading=heading,JointRefused=true};
                        frame.Proposals[peer]=new NavMotionProposal{Position=receiver.Position,Heading=heading,JointRefused=refusal=="joint",Blocked=refusal=="blocked",StaticNoWinner=refusal=="static"};
                        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;typeof(NavCrowd).GetMethod("BuildIndex",flags).Invoke(frame.Working,null);
                        var retry=typeof(NavCrowd).GetMethod("RetryPolicyStoppedYield",flags);var bodies=(NavMotionBody[])frame.Motion.Clone();var proposals=(NavMotionProposal[])frame.Proposals.Clone();
                        var headers=frame.After.Select(u=>{var h=new NavUnitSaveState();u.CopyCaptureHeaderTo(h);return h;}).ToArray();
                        void Reset(){Array.Copy(bodies,frame.Motion,bodies.Length);Array.Copy(proposals,frame.Proposals,proposals.Length);Array.Clear(frame.Formation,0,frame.Formation.Length);actor.noProgressTicks=receiver.noProgressTicks=10;for(int i=0;i<frame.YieldLabels.Length;i++)frame.YieldLabels[i]=0;}
                        void Retry()=>retry.Invoke(frame.Working,new object[]{1d/30,frame.Motion,frame.Proposals,frame.YieldLabels,frame.YieldQueue,frame.NeighborCounts});
                        foreach(string negative in new[]{"fresh-peer","useful-peer-turn","peer-wait","moving-peer"}){
                            Reset();if(negative=="fresh-peer")receiver.noProgressTicks=0;if(negative=="peer-wait")frame.Formation[peer].Flags=NavFormationFrameHint.Wait;
                            if(negative=="useful-peer-turn")frame.Proposals[peer]=new NavMotionProposal{Position=receiver.Position,Heading=heading+.01,IndependentTurn=true};
                            if(negative=="moving-peer")frame.Proposals[peer]=new NavMotionProposal{Position=new NavPoint(receiver.Position.X+direction.X/100,receiver.Position.Z+direction.Z/100),Velocity=new NavPoint(direction.X*.3,direction.Z*.3),Heading=heading};
                            var expected=(NavMotionProposal[])frame.Proposals.Clone();Retry();CollectionAssert.AreEqual(expected,frame.Proposals,negative);Assert.False(frame.Motion[root].Yielding,negative);Assert.False(frame.Motion[peer].Yielding,negative);
                        }
                        Reset();Retry();
                        Assert.True(frame.Motion[root].Yielding);Assert.True(frame.Motion[peer].Yielding);Assert.Greater(frame.Motion[root].YieldLabel,0);Assert.AreEqual(frame.Motion[root].YieldLabel,frame.Motion[peer].YieldLabel);
                        foreach(int row in new[]{root,peer}){var body=frame.Motion[row];var next=frame.Proposals[row];Assert.AreNotEqual(body.Position,next.Position);Assert.LessOrEqual(NavCrowdMotion.Length(next.Velocity),bodies[row].Speed);Assert.True(frame.RecastReceipts[row].Verified);Assert.AreEqual(next.Position,frame.RecastReceipts[row].To);Assert.True(frame.After[row].SameCaptureHeader(headers[row]));Assert.AreEqual(10,frame.After[row].noProgressTicks);}
                    }finally{frame.Release();}
                }
                CollectionAssert.AreEqual(before,Bytes());Assert.AreEqual(native,RecastNative.sw_recast_allocation_count());Assert.AreEqual(faults,RecastNative.ReleaseFaults);
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(Budget(s).PhysicalBytes);Assert.Zero(Budget(s).HeldBytes);Assert.Zero(Budget(s).ActiveScopes);}
        }
        [TestCase(false)][TestCase(true)] public void SelectPreservesOnlyPhysicallyQualifiedRowCapWaitThroughOwnerGuards(bool scalar)
        {
            var s=Ready();bool old=NavCrowdMotion.DisableSelectClearanceReuseForQa;NavCrowdMotion.DisableSelectClearanceReuseForQa=scalar;
            try{var route=Route(s);Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,r);
                try{frame.Observe();int row=frame.Find(2);var actor=frame.After[row];frame.BindRecast(row,route,NavRecastIntervals.Initial(route));
                    int portal=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);Assert.True(RecastFormationMotion.Axis(route,portal,out var origin,out _,out var direction,out _));
                    double station=RecastFormationMotion.Station(actor.Position,origin,direction);var target=new NavPoint(actor.Position.X+direction.X*frame.Profile.ArrivalSlotSpacing,actor.Position.Z+direction.Z*frame.Profile.ArrivalSlotSpacing);
                    var hint=new NavFormationFrameHint{Flags=NavFormationFrameHint.Bound,TargetLeg=portal,ForwardLimit=station-frame.Profile.ArrivalSlotSpacing};frame.Formation[row]=hint;
                    var body=new NavMotionBody{Id=actor.Id,Position=actor.Position,Target=target,Heading=Math.Atan2(direction.Z,direction.X),Radius=actor.Radius,Speed=actor.Speed,TurnSpeed=actor.TurnSpeed,Active=true,OwnMoving=true};frame.Motion[row]=body;actor.noProgressTicks=148;actor.blockedSeconds=4d/30;
                    var neighbors=new int[frame.Motion.Length*NavCrowdMotion.NeighborCapacity];var selected=NavCrowdMotion.Select(frame.Motion,row,neighbors,0,frame.Profile,1d/30,frame.Working,actor.Goal,false,target);
                    Assert.True(selected.RoleCapWait,"The real selector must retain the cap reason when its screened result is stationary");Assert.True(selected.StaticNoWinner);Assert.AreEqual(body.Position,selected.Position);Assert.AreEqual(default(NavPoint),selected.Velocity);
                    frame.Proposals[row]=selected;typeof(NavCrowd).GetMethod("RecordInitialRecastPolicyWait",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(frame.Working,new object[]{frame.Motion,frame.Proposals});
                    Assert.AreNotEqual(0,frame.Formation[row].Flags&NavFormationFrameHint.PolicyWait);NavCrowdMotion.ResolveJoint(frame.Motion,frame.Proposals,1d/30);
                    typeof(NavCrowd).GetMethod("FilterRecastSweeps",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(frame.Working,new object[]{1d/30,frame.Motion,frame.Proposals});
                    Assert.AreEqual(selected,frame.Proposals[row]);Assert.AreEqual(148,actor.noProgressTicks);Assert.AreEqual(4d/30,actor.blockedSeconds);
                    // An actually enclosed actor has no physical candidate to
                    // attribute to the cap, even while the same row cap exists.
                    frame.Formation[row]=hint;var enclosed=new NavMotionBody[row+9];enclosed[row]=body;
                    for(int n=0;n<8;n++){int index=row+1+n;double angle=n*Math.PI/4;enclosed[index]=new NavMotionBody{Id=1000+n,Position=new NavPoint(body.Position.X+2*body.Radius*Math.Cos(angle),body.Position.Z+2*body.Radius*Math.Sin(angle)),Radius=body.Radius};neighbors[row*NavCrowdMotion.NeighborCapacity+n]=index;}
                    var blocked=NavCrowdMotion.Select(enclosed,row,neighbors,8,frame.Profile,1d/30,frame.Working,actor.Goal,false,target);Assert.False(blocked.RoleCapWait,"A physical enclosure must keep its real no-progress reason");Assert.True(blocked.Blocked||blocked.StaticNoWinner);
                    hint.ForwardLimit=station+2*frame.Profile.ArrivalSlotSpacing;frame.Formation[row]=hint;
                    var released=NavCrowdMotion.Select(frame.Motion,row,neighbors,0,frame.Profile,1d/30,frame.Working,actor.Goal,false,target);Assert.False(released.RoleCapWait);Assert.AreNotEqual(body.Position,released.Position,"Opening the live cap must immediately restore legal motion");
                }finally{frame.Release();}scope.Discard();}
            }finally{NavCrowdMotion.DisableSelectClearanceReuseForQa=old;s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(Budget(s).PhysicalBytes);Assert.Zero(Budget(s).HeldBytes);Assert.Zero(Budget(s).ActiveScopes);}
        }
        [Test] public void OwnRoleWaitSurvivesAnEarlierJointRetractionWithoutChangingItsClockOrProposal()
        {
            var s=Ready();byte[] Bytes(){using var state=s.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                var before=Bytes();var route=Route(s);Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,r);
                    try{
                        frame.Observe();int row=frame.Find(2),peer=frame.Find(1);var actor=frame.After[row];frame.BindRecast(row,route,NavRecastIntervals.Initial(route));
                        int portal=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);Assert.True(RecastFormationMotion.Axis(route,portal,out var origin,out _,out var direction,out _));
                        double station=RecastFormationMotion.Station(actor.Position,origin,direction);var end=new NavPoint(actor.Position.X+direction.X*actor.Speed/30,actor.Position.Z+direction.Z*actor.Speed/30);
                        var hint=new NavFormationFrameHint{Flags=NavFormationFrameHint.Bound,TargetLeg=portal,ForwardLimit=station-frame.Profile.ArrivalSlotSpacing};frame.Formation[row]=hint;
                        frame.Motion[row]=new NavMotionBody{Id=actor.Id,Position=actor.Position,Radius=actor.Radius,Speed=actor.Speed,Active=true,OwnMoving=true};
                        var receiver=frame.After[peer];double gap=actor.Radius+receiver.Radius+.02;var at=new NavPoint(actor.Position.X+direction.X*gap,actor.Position.Z+direction.Z*gap);
                        frame.Motion[peer]=new NavMotionBody{Id=receiver.Id,Position=at,Radius=receiver.Radius,Active=true,OwnMoving=true};
                        frame.Proposals[row]=new NavMotionProposal{Position=end,Velocity=new NavPoint(direction.X*actor.Speed,direction.Z*actor.Speed)};frame.Proposals[peer]=new NavMotionProposal{Position=at};actor.noProgressTicks=148;actor.blockedSeconds=4d/30;
                        var body=frame.Motion[row];var selected=frame.Proposals[row];long physical=Budget(s).PhysicalBytes;var record=typeof(NavCrowd).GetMethod("RecordInitialRecastPolicyWait",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                        void Record()=>record.Invoke(frame.Working,new object[]{frame.Motion,frame.Proposals});
                        foreach(string negative in new[]{"allowed","stationary","no-route","inactive","not-own","yielding","producer","overflow"}){
                            try{
                                frame.Formation[row]=hint;frame.Motion[row]=body;frame.Proposals[row]=selected;
                                if(negative=="allowed")frame.Formation[row].ForwardLimit=station+frame.Profile.ArrivalSlotSpacing;if(negative=="stationary")frame.Proposals[row].Position=body.Position;
                                if(negative=="no-route")frame.RecastRoutes[row]=null;if(negative=="inactive")frame.Motion[row].Active=false;if(negative=="not-own")frame.Motion[row].OwnMoving=false;
                                if(negative=="yielding")frame.Motion[row].Yielding=true;if(negative=="producer")frame.Motion[row].YieldLabel=-1;if(negative=="overflow")frame.Proposals[row].Overflow=true;
                                var expected=frame.Proposals[row];Record();Assert.Zero(frame.Formation[row].Flags&NavFormationFrameHint.PolicyWait,negative);Assert.AreEqual(expected,frame.Proposals[row],negative);
                            }finally{frame.RecastRoutes[row]=route;}
                        }
                        frame.Formation[row]=hint;frame.Motion[row]=body;frame.Proposals[row]=selected;Record();Assert.AreNotEqual(0,frame.Formation[row].Flags&NavFormationFrameHint.PolicyWait);Assert.AreEqual(selected,frame.Proposals[row]);Assert.AreEqual(physical,Budget(s).PhysicalBytes,"Recording policy is allocation-free before the existing paid sweep guards");
                        NavCrowdMotion.ResolveJoint(frame.Motion,frame.Proposals,1d/30);Assert.True(frame.Proposals[row].JointRefused);Assert.AreEqual(actor.Position,frame.Proposals[row].Position);
                        typeof(NavCrowd).GetMethod("FilterRecastSweeps",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(frame.Working,new object[]{1d/30,frame.Motion,frame.Proposals});
                        Assert.AreNotEqual(0,frame.Formation[row].Flags&NavFormationFrameHint.PolicyWait);Assert.True(frame.RecastReceipts[row].Verified);Assert.AreEqual(148,actor.noProgressTicks);Assert.AreEqual(4d/30,actor.blockedSeconds);CollectionAssert.AreEqual(before,Bytes());
                    }finally{frame.Release();}scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(Budget(s).PhysicalBytes);Assert.Zero(Budget(s).HeldBytes);Assert.Zero(Budget(s).ActiveScopes);}
        }
        [Test] public void ActualStationaryNormalPeerAllowsOneGuardedRetryAndRestoresAllActiveFlags()
        {
            ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;int savedActor=NavCrowdMotion.CausalActor;var savedCausal=NavCrowdMotion.Causal;
            var s=Ready();byte[] Bytes(){using var state=s.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                var before=Bytes();int publications=s.OwnedMovementPublications;var route=Route(s);
                Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,r);
                    try{
                        frame.Observe();int row=frame.Find(2),peer=frame.Find(1);var actor=frame.After[row];var receiver=frame.After[peer];var state=NavRecastIntervals.Initial(route);frame.BindRecast(row,route,state);
                        // Keep the same accepted bank route, with forecast room
                        // for a positive avoidance control instead of a narrow edge.
                        actor.Position=new NavPoint(actor.Position.X+2*frame.Profile.ArrivalSlotSpacing,actor.Position.Z);actor.Location=new NavLocation(actor.Position,actor.Location.NameReference);
                        Assert.True(s.Crowd.Geometry.IsFree(actor.Position,actor.Radius));Assert.True(PlayableProfile.RecastGroupCorridorDefault.AuthoredMap.IsValidLocation(actor.Location,actor.Radius));
                        var target=actor.route[actor.routeIndex];double dx=target.X-actor.Position.X,dz=target.Z-actor.Position.Z,length=Math.Sqrt(dx*dx+dz*dz);Assert.Greater(length,0);var direction=new NavPoint(dx/length,dz/length);double heading=Math.Atan2(dz,dx),distance=actor.Radius+receiver.Radius+.02;
                        receiver.Position=new NavPoint(actor.Position.X+direction.X*distance,actor.Position.Z+direction.Z*distance);receiver.Location=new NavLocation(receiver.Position,actor.Location.NameReference);actor.Heading=heading;actor.noProgressTicks=10;actor.blockedSeconds=10d/30;
                        Assert.True(s.Crowd.Geometry.IsFree(receiver.Position,receiver.Radius));Assert.True(PlayableProfile.RecastGroupCorridorDefault.AuthoredMap.IsValidLocation(receiver.Location,receiver.Radius));
                        int portal=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);Assert.True(RecastFormationMotion.Axis(route,portal,out var origin,out _,out var axis,out _));
                        frame.Formation[row]=new NavFormationFrameHint{Flags=NavFormationFrameHint.Guide|NavFormationFrameHint.Bound,Target=target,TargetLeg=portal,ForwardLimit=RecastFormationMotion.Station(actor.Position,origin,axis)+s.Crowd.Profile.ArrivalSlotSpacing};
                        frame.Motion[row]=new NavMotionBody{Id=actor.Id,Position=actor.Position,Target=target,Heading=heading,Radius=actor.Radius,Speed=actor.Speed,TurnSpeed=actor.TurnSpeed,Active=true,OwnMoving=true,OwnRouteIndex=actor.routeIndex,OwnLocalIndex=actor.localIndex};
                        frame.Motion[peer]=new NavMotionBody{Id=receiver.Id,Position=receiver.Position,Target=new NavPoint(receiver.Position.X+direction.X*length,receiver.Position.Z+direction.Z*length),Heading=heading,Radius=receiver.Radius,Speed=actor.Speed,TurnSpeed=receiver.TurnSpeed,Active=true,OwnMoving=true};
                        frame.Neighbors[row*NavCrowdMotion.NeighborCapacity]=peer;frame.Neighbors[peer*NavCrowdMotion.NeighborCapacity]=row;frame.NeighborCounts[row]=frame.NeighborCounts[peer]=1;
                        frame.Proposals[row]=NavCrowdMotion.Select(frame.Motion,row,frame.Neighbors,1,frame.Profile,1d/30,frame.Working,actor.Goal);frame.Proposals[peer]=new NavMotionProposal{Position=receiver.Position,Heading=heading};
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="controlled-normalzero-beforeJoint",Actor=actor.Id,Peer=receiver.Id,actor.Position,PeerPosition=receiver.Position,Selected=frame.Proposals[row].Position,Velocity=frame.Proposals[row].Velocity,SessionSteps=0}));
                        Assert.AreEqual(frame.Proposals[row],NavCrowdMotion.Select(frame.Motion,row,frame.Neighbors,1,frame.Profile,1d/30,frame.Working,actor.Goal,true),"Opt-in must preserve an existing normal winner");
                        Assert.AreNotEqual(default(NavPoint),frame.Proposals[row].Velocity);Assert.True(NavRecastIntervals.Advance(route,state,actor.Location,frame.Proposals[row].Position,r,out _,frame.Working,actor.Id));
                        NavCrowdMotion.CausalActor=actor.Id;NavCrowdMotion.Causal=default;NavCrowdMotion.ResolveJoint(frame.Motion,frame.Proposals,1d/30);Assert.True(frame.Proposals[row].JointRefused);Assert.AreEqual(actor.Position,frame.Proposals[row].Position);Assert.AreEqual(receiver.Id,NavCrowdMotion.Causal.Last.Peer);
                        NavMotionProposal defaultStatic;
                        frame.Motion[peer].Active=false;
                        try{defaultStatic=NavCrowdMotion.Select(frame.Motion,row,frame.Neighbors,1,frame.Profile,1d/30,frame.Working,actor.Goal);}
                        finally{frame.Motion[peer].Active=true;}
                        Assert.AreEqual(frame.Proposals[row].Position,defaultStatic.Position);Assert.AreEqual(frame.Proposals[row].Heading,defaultStatic.Heading,"Default opt-out keeps the recorded no useful winner");
                        var causal=NavCrowdMotion.Causal;var bodies=(NavMotionBody[])frame.Motion.Clone();var proposals=(NavMotionProposal[])frame.Proposals.Clone();var hints=(NavFormationFrameHint[])frame.Formation.Clone();var headers=frame.After.Select(a=>{var h=new NavUnitSaveState();a.CopyCaptureHeaderTo(h);return h;}).ToArray();
                        var retry=typeof(NavCrowd).GetMethod("RetryPolicyStoppedPeers",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                        void Retry()=>retry.Invoke(frame.Working,new object[]{1d/30,frame.Motion,frame.Proposals,frame.Neighbors,frame.NeighborCounts,frame.YieldQueue});
                        void Reset(){Array.Copy(bodies,frame.Motion,bodies.Length);Array.Copy(proposals,frame.Proposals,proposals.Length);Array.Copy(hints,frame.Formation,hints.Length);frame.NeighborCounts[row]=1;actor.Held=receiver.Held=false;frame.RecastRoutes[row]=route;NavCrowdMotion.Causal=causal;}
                        object Proposal(NavMotionProposal q)=>new{q.Position,q.Velocity,q.Heading,q.IndependentTurn,q.Blocked,q.StaticNoWinner,q.JointRefused};
                        Retry();TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="static-goal-turn-before-positive-assert",DefaultStatic=Proposal(defaultStatic),NavCrowdMotion.Causal.RetryAttempted,NavCrowdMotion.Causal.RetryAccepted,NavCrowdMotion.Causal.RetryGoalTurn,RetrySelected=Proposal(NavCrowdMotion.Causal.RetrySelected),Final=Proposal(frame.Proposals[row]),PeerFinal=Proposal(frame.Proposals[peer]),OwnActive=frame.Motion[row].Active,PeerActive=frame.Motion[peer].Active,TypedVerified=frame.RecastReceipts[row].Verified,HintFlags=frame.Formation[row].Flags,actor.noProgressTicks,SessionSteps=0}));
                        Assert.True(NavCrowdMotion.Causal.RetryAttempted,"The own candidate is itself in the stopped set and must not be skipped");Assert.True(NavCrowdMotion.Causal.RetryAccepted);Assert.True(NavCrowdMotion.Causal.RetryGoalTurn);Assert.True(NavCrowdMotion.Causal.RetryPeerInitiallyZero);Assert.True(NavCrowdMotion.Causal.RetryPeerMatchedRefuser);Assert.AreEqual(receiver.Id,NavCrowdMotion.Causal.RetryPeerId);Assert.True(NavCrowdMotion.Causal.RetryPeerBody.Active);Assert.True(NavCrowdMotion.Causal.RetryPeerBody.OwnMoving);
                        var selected=NavCrowdMotion.Causal.RetrySelected;var final=frame.Proposals[row];Assert.True(final.JointRefused);Assert.True(!final.Position.Equals(proposals[row].Position)||final.Heading!=proposals[row].Heading,"Positive fallback must obtain a safe turn or translation, not a skipped old proposal");
                        Assert.LessOrEqual(NavCrowdMotion.Length(final.Velocity),actor.Speed+1e-12);Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(final.Heading-heading)),actor.TurnSpeed/30+1e-12);
                        Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(new NavPoint(actor.Position.X-receiver.Position.X,actor.Position.Z-receiver.Position.Z),new NavPoint(final.Velocity.X-frame.Proposals[peer].Velocity.X,final.Velocity.Z-frame.Proposals[peer].Velocity.Z),1d/30),Math.Pow(actor.Radius+receiver.Radius,2)-1e-9);
                        Assert.True(RecastFormationMotion.Allows(hints[row],route,actor.Position,final.Position));Assert.True(frame.RecastReceipts[row].Verified);CollectionAssert.AreEqual(bodies,frame.Motion);Assert.AreEqual(proposals[peer],frame.Proposals[peer]);for(int i=0;i<headers.Length;i++)Assert.True(frame.After[i].SameCaptureHeader(headers[i]));
                        foreach(string negative in new[]{"inactive-own","own-wait","own-policy-wait","held-own","held-peer","yield-own","yield-peer","overflow","unknown-q9","missing-q9","moving-peer","receiver-not-own","no-refusal","no-route"}){
                            try{
                                Reset();if(negative=="inactive-own")frame.Motion[row].Active=false;if(negative=="own-wait")frame.Formation[row].Flags|=NavFormationFrameHint.Wait;if(negative=="own-policy-wait")frame.Formation[row].Flags|=NavFormationFrameHint.PolicyWait;
                                if(negative=="held-own")actor.Held=true;if(negative=="held-peer")receiver.Held=true;if(negative=="yield-own")frame.Motion[row].Yielding=true;if(negative=="yield-peer")frame.Motion[peer].Yielding=true;
                                if(negative=="overflow")frame.Proposals[row].Overflow=true;if(negative=="unknown-q9")frame.NeighborCounts[row]=-1;if(negative=="missing-q9")frame.NeighborCounts[row]=0;
                                if(negative=="moving-peer")frame.Proposals[peer]=new NavMotionProposal{Position=new NavPoint(receiver.Position.X+direction.X*actor.Speed/30,receiver.Position.Z+direction.Z*actor.Speed/30),Velocity=new NavPoint(direction.X*actor.Speed,direction.Z*actor.Speed)};
                                if(negative=="receiver-not-own")frame.Motion[peer].OwnMoving=false;if(negative=="no-refusal")frame.Proposals[row].JointRefused=false;if(negative=="no-route")frame.RecastRoutes[row]=null;
                                var expectedBodies=(NavMotionBody[])frame.Motion.Clone();var expectedProposals=(NavMotionProposal[])frame.Proposals.Clone();Retry();Assert.False(NavCrowdMotion.Causal.RetryAttempted,negative);CollectionAssert.AreEqual(expectedBodies,frame.Motion,negative);CollectionAssert.AreEqual(expectedProposals,frame.Proposals,negative);
                            }finally{Reset();}
                        }
                        // A normal stationary turn after prior actual refusal must
                        // use the same stopped-peer forecast as the previous retry,
                        // rather than turning back toward that peer every other frame.
                        Reset();frame.Proposals[row].JointRefused=false;
                        frame.Proposals[row].Heading=NavCrowdMotion.Turn(heading,heading+Math.PI/2,actor.TurnSpeed/30);
                        Retry();Assert.True(NavCrowdMotion.Causal.RetryAttempted);Assert.True(NavCrowdMotion.Causal.RetryAccepted);
                        Assert.AreEqual(selected,NavCrowdMotion.Causal.RetrySelected);Assert.False(frame.Proposals[row].JointRefused);
                        Assert.True(frame.RecastReceipts[row].Verified);CollectionAssert.AreEqual(bodies,frame.Motion);
                        Assert.AreEqual(10,actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds);
                        Reset();frame.Proposals[row].JointRefused=false;frame.Proposals[row].Heading=heading+actor.TurnSpeed/30;
                        var normalTurn=frame.Proposals[row];var savedGoal=actor.Goal;
                        // Exhaust the optional goal-improving fallback. This
                        // detached negative control never publishes a new goal.
                        actor.Goal=actor.Position;
                        try{Retry();Assert.True(NavCrowdMotion.Causal.RetryAttempted);Assert.True(NavCrowdMotion.Causal.RetrySelected.StaticNoWinner);Assert.False(NavCrowdMotion.Causal.RetryAccepted);Assert.AreEqual(normalTurn,frame.Proposals[row],"No-winner retry must preserve the original normal turn");CollectionAssert.AreEqual(bodies,frame.Motion);}
                        finally{actor.Goal=savedGoal;Reset();}
                        Reset();frame.Proposals[row].JointRefused=false;frame.Proposals[row].Heading=heading+actor.TurnSpeed/30;actor.noProgressTicks=0;
                        try{Retry();Assert.False(NavCrowdMotion.Causal.RetryAttempted,"A fresh normal turn has no previous actual stall");}
                        finally{actor.noProgressTicks=10;Reset();}
                        // The same fresh turn IS part of a physical hold cycle
                        // when its stationary footprint has actually refused a
                        // touching normal peer in the real joint sweep.
                        var contactPeerPosition=receiver.Position;var contactPeerLocation=receiver.Location;
                        try{
                            Reset();actor.noProgressTicks=0;frame.Proposals[row].JointRefused=false;frame.Proposals[row].Heading=heading+actor.TurnSpeed/30;
                            frame.Motion[peer].Heading=NavCrowdMotion.Angle(heading+Math.PI);frame.Motion[peer].Target=actor.Position;
                            var peerVelocity=new NavPoint(-direction.X*receiver.Speed,-direction.Z*receiver.Speed);
                            frame.Proposals[peer]=new NavMotionProposal{Position=new NavPoint(receiver.Position.X+peerVelocity.X/30,receiver.Position.Z+peerVelocity.Z/30),Velocity=peerVelocity,Heading=frame.Motion[peer].Heading};
                            NavCrowdMotion.ResolveJoint(frame.Motion,frame.Proposals,1d/30);Assert.True(frame.Proposals[peer].JointRefused);Assert.True(frame.Proposals[peer].HasJointRefuser);Assert.AreEqual(actor.Id,frame.Proposals[peer].JointFirstRefuser);Assert.AreEqual(receiver.Position,frame.Proposals[peer].Position);Assert.True(frame.Working.PhysicalSupportContact(actor,receiver));
                            var contactBodies=(NavMotionBody[])frame.Motion.Clone();Retry();Assert.True(NavCrowdMotion.Causal.RetryAttempted,"The physically refused contact supplies cohort provenance without inventing an own failed step");Assert.True(NavCrowdMotion.Causal.RetryAccepted);Assert.True(frame.RecastReceipts[row].Verified);Assert.Zero(actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds);CollectionAssert.AreEqual(contactBodies,frame.Motion);
                            int debugActor=NavCrowdMotion.CausalActor;
                            try{
                                Reset();NavCrowdMotion.CausalActor=0;actor.noProgressTicks=0;frame.Proposals[row].JointRefused=false;frame.Proposals[row].Heading=heading+actor.TurnSpeed/30;
                                frame.Motion[peer].Heading=NavCrowdMotion.Angle(heading+Math.PI);frame.Motion[peer].Target=actor.Position;
                                frame.Proposals[peer]=new NavMotionProposal{Position=new NavPoint(receiver.Position.X+peerVelocity.X/30,receiver.Position.Z+peerVelocity.Z/30),Velocity=peerVelocity,Heading=frame.Motion[peer].Heading};
                                var debugCausal=NavCrowdMotion.Causal;NavCrowdMotion.ResolveJoint(frame.Motion,frame.Proposals,1d/30);
                                Assert.True(frame.Proposals[peer].HasJointRefuser);Assert.AreEqual(actor.Id,frame.Proposals[peer].JointFirstRefuser,"Production provenance must exist with debug OFF");var fresh=frame.Proposals[row];
                                Retry();Assert.AreNotEqual(fresh.Heading,frame.Proposals[row].Heading,"The actual causal turn must be reselected with debug OFF");Assert.True(frame.RecastReceipts[row].Verified);Assert.Zero(actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds);Assert.AreEqual(debugCausal,NavCrowdMotion.Causal,"Debug recording remains off throughout real Joint and retry");
                            }finally{NavCrowdMotion.CausalActor=debugActor;Reset();}
                            // A touching peer actually refused by a THIRD body
                            // cannot reinterpret this unrelated fresh own turn.
                            Reset();actor.noProgressTicks=0;frame.Proposals[row].JointRefused=false;frame.Proposals[row].Heading=heading+actor.TurnSpeed/30;
                            var tangent=new NavPoint(-direction.Z,direction.X);var away=new NavPoint(tangent.X*receiver.Speed,tangent.Z*receiver.Speed);
                            var third=new NavMotionBody{Id=10001,Position=new NavPoint(receiver.Position.X+tangent.X*distance,receiver.Position.Z+tangent.Z*distance),Radius=receiver.Radius};
                            var threeBodies=new[]{frame.Motion[row],frame.Motion[peer],third};var threeProposals=new[]{frame.Proposals[row],new NavMotionProposal{Position=new NavPoint(receiver.Position.X+away.X/30,receiver.Position.Z+away.Z/30),Velocity=away},new NavMotionProposal{Position=third.Position}};
                            NavCrowdMotion.ResolveJoint(threeBodies,threeProposals,1d/30);Assert.True(threeProposals[1].JointRefused);Assert.True(threeProposals[1].HasJointRefuser);Assert.AreEqual(third.Id,threeProposals[1].JointFirstRefuser);frame.Proposals[peer]=threeProposals[1];
                            Retry();Assert.False(NavCrowdMotion.Causal.RetryAttempted,"Contact alone must not promote a peer's refusal by a third body");Assert.Zero(actor.noProgressTicks);
                            Reset();actor.noProgressTicks=0;frame.Proposals[row].JointRefused=false;frame.Proposals[row].Heading=heading+actor.TurnSpeed/30;frame.Proposals[peer].JointRefused=true;
                            Retry();Assert.False(NavCrowdMotion.Causal.RetryAttempted,"An unbound refusal flag is not causal evidence");
                            Reset();actor.noProgressTicks=0;frame.Proposals[row].JointRefused=false;frame.Proposals[row].Heading=heading+actor.TurnSpeed/30;frame.Proposals[peer].JointRefused=true;
                            receiver.Position=new NavPoint(receiver.Position.X+direction.X*frame.Profile.ArrivalSlotSpacing,receiver.Position.Z+direction.Z*frame.Profile.ArrivalSlotSpacing);receiver.Location=new NavLocation(receiver.Position,contactPeerLocation.NameReference);frame.Motion[peer].Position=receiver.Position;frame.Proposals[peer].Position=receiver.Position;
                            Retry();Assert.False(NavCrowdMotion.Causal.RetryAttempted,"An unrelated distant refusal must not reinterpret a fresh normal turn");Assert.Zero(actor.noProgressTicks);
                        }finally{receiver.Position=contactPeerPosition;receiver.Location=contactPeerLocation;actor.noProgressTicks=10;Reset();}
                        // A completed installed repair is still evidence of the
                        // earlier stall after physical progress cleared its clock.
                        var retainedLocal=actor.localRoute;int retainedLocalIndex=actor.localIndex;
                        try{
                            Reset();frame.Proposals[row].JointRefused=false;frame.Proposals[row].Heading=heading+actor.TurnSpeed/30;actor.noProgressTicks=0;
                            actor.localRoute=new[]{actor.Position};actor.localIndex=1;
                            Retry();Assert.True(NavCrowdMotion.Causal.RetryAttempted);Assert.True(NavCrowdMotion.Causal.RetryAccepted);
                            Assert.True(frame.RecastReceipts[row].Verified);Assert.Zero(actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds);
                            CollectionAssert.AreEqual(bodies,frame.Motion);
                        }finally{actor.localRoute=retainedLocal;actor.localIndex=retainedLocalIndex;actor.noProgressTicks=10;Reset();}
                        // Preserve the original policy-only receiver (not OwnMoving)
                        // and distinguish a q9 stopped witness from a real refuser.
                        frame.Motion[peer].OwnMoving=false;frame.Formation[peer].Flags=NavFormationFrameHint.PolicyWait;NavCrowdMotion.Causal.Last=default;Retry();Assert.True(NavCrowdMotion.Causal.RetryAccepted);Assert.False(NavCrowdMotion.Causal.RetryPeerMatchedRefuser);Assert.AreEqual(selected,NavCrowdMotion.Causal.RetrySelected);Assert.True(frame.Motion[row].Active&&frame.Motion[peer].Active);
                        Reset();long physical=Budget(s).PhysicalBytes,held=Budget(s).HeldBytes;int scopes=Budget(s).ActiveScopes,denials=0;long deniedBytes=0;bool guardFlagsTemporary=false;Assert.IsNull(Budget(s).AllocationAdmission);
                        try{Budget(s).AllocationAdmission=bytes=>{denials++;deniedBytes=bytes;guardFlagsTemporary=!frame.Motion[row].Active&&!frame.Motion[peer].Active;return false;};var error=Assert.Throws<System.Reflection.TargetInvocationException>(Retry);Assert.That(error.InnerException,Is.TypeOf<InvalidOperationException>());Assert.AreEqual("Scratch exceeds prepared reservation.",error.InnerException.Message);}
                        finally{Budget(s).AllocationAdmission=null;}
                        Assert.True(guardFlagsTemporary,"Own Active is restored only for Select, not subsequent typed guards");Assert.True(NavCrowdMotion.Causal.RetryAttempted);Assert.False(NavCrowdMotion.Causal.RetryAccepted);Assert.AreEqual(selected,NavCrowdMotion.Causal.RetrySelected,"Select completed; denial was the subsequent needed typed guard");Assert.AreEqual(1,denials);Assert.Greater(deniedBytes,NavStorageScratch.LeaseBytes);
                        CollectionAssert.AreEqual(bodies,frame.Motion);CollectionAssert.AreEqual(proposals,frame.Proposals);CollectionAssert.AreEqual(hints,frame.Formation);for(int i=0;i<headers.Length;i++)Assert.True(frame.After[i].SameCaptureHeader(headers[i]));Assert.AreEqual(physical,Budget(s).PhysicalBytes);Assert.AreEqual(held,Budget(s).HeldBytes);Assert.AreEqual(scopes,Budget(s).ActiveScopes);Assert.AreEqual(publications,s.OwnedMovementPublications);CollectionAssert.AreEqual(before,Bytes());
                        // A fresh default Select sees this peer inactive from the start.
                        // This is a generic guard proof, not a replay of recorded actor 77.
                        Reset();frame.Motion[peer].Active=false;NavCrowdMotion.Causal=default;
                        frame.Proposals[row]=NavCrowdMotion.Select(frame.Motion,row,frame.Neighbors,1,frame.Profile,1d/30,frame.Working,actor.Goal);
                        bodies=(NavMotionBody[])frame.Motion.Clone();proposals=(NavMotionProposal[])frame.Proposals.Clone();causal=NavCrowdMotion.Causal;
                        var original=frame.Proposals[row];
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="original-inactive-default-before-assert",Original=Proposal(original),PeerOriginalActive=bodies[peer].Active,SessionSteps=0}));
                        Assert.True(original.StaticNoWinner);Assert.False(original.JointRefused);Assert.AreEqual(actor.Position,original.Position);Assert.AreEqual(default(NavPoint),original.Velocity);Assert.AreEqual(heading,original.Heading);
                        Retry();selected=NavCrowdMotion.Causal.RetrySelected;final=frame.Proposals[row];
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="original-static-no-winner-before-positive-assert",Original=Proposal(original),NavCrowdMotion.Causal.RetryAttempted,NavCrowdMotion.Causal.RetryAccepted,NavCrowdMotion.Causal.RetryGoalTurn,RetrySelected=Proposal(selected),Final=Proposal(final),PeerOriginalActive=NavCrowdMotion.Causal.RetryPeerBody.Active,PeerFinalActive=frame.Motion[peer].Active,actor.noProgressTicks,actor.blockedSeconds,SessionSteps=0}));
                        Assert.True(NavCrowdMotion.Causal.RetryAttempted);Assert.True(NavCrowdMotion.Causal.RetryAccepted);Assert.True(NavCrowdMotion.Causal.RetryGoalTurn);Assert.False(NavCrowdMotion.Causal.RetryPeerBody.Active);Assert.False(frame.Motion[peer].Active);Assert.False(final.JointRefused);Assert.True(final.StaticNoWinner);Assert.AreEqual(actor.Position,final.Position);Assert.AreEqual(default(NavPoint),final.Velocity);Assert.AreNotEqual(heading,final.Heading);
                        Assert.True((final.Blocked||final.StaticNoWinner||final.JointRefused)&&!final.Overflow,"Unchanged stationary apply counter predicate remains true");Assert.AreEqual(10,actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds,"Private retry does not apply counters; real Step/reset is covered separately");
                        Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(final.Heading-heading)),actor.TurnSpeed/30+1e-12);Assert.LessOrEqual(NavCrowdMotion.Length(final.Velocity),actor.Speed+1e-12);Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(new NavPoint(actor.Position.X-receiver.Position.X,actor.Position.Z-receiver.Position.Z),final.Velocity,1d/30),Math.Pow(actor.Radius+receiver.Radius,2)-1e-9);
                        Assert.True(RecastFormationMotion.Allows(hints[row],route,actor.Position,final.Position));Assert.True(frame.RecastReceipts[row].Verified);CollectionAssert.AreEqual(bodies,frame.Motion);Assert.AreEqual(proposals[peer],frame.Proposals[peer]);
                        foreach(string negative in new[]{"inactive-own","own-wait","own-policy-wait","held-own","held-peer","yield-own","yield-peer","peer-label","overflow","unknown-q9","missing-q9","moving-peer","no-original-failure","unaligned","no-route"}){
                            try{
                                Reset();if(negative=="inactive-own")frame.Motion[row].Active=false;if(negative=="own-wait")frame.Formation[row].Flags|=NavFormationFrameHint.Wait;if(negative=="own-policy-wait")frame.Formation[row].Flags|=NavFormationFrameHint.PolicyWait;
                                if(negative=="held-own")actor.Held=true;if(negative=="held-peer")receiver.Held=true;if(negative=="yield-own")frame.Motion[row].Yielding=true;if(negative=="yield-peer")frame.Motion[peer].Yielding=true;if(negative=="peer-label")frame.Motion[peer].YieldLabel=1;
                                if(negative=="overflow")frame.Proposals[row].Overflow=true;if(negative=="unknown-q9")frame.NeighborCounts[row]=-1;if(negative=="missing-q9")frame.NeighborCounts[row]=0;
                                if(negative=="moving-peer")frame.Proposals[peer]=new NavMotionProposal{Position=new NavPoint(receiver.Position.X+direction.X*actor.Speed/30,receiver.Position.Z+direction.Z*actor.Speed/30),Velocity=new NavPoint(direction.X*actor.Speed,direction.Z*actor.Speed)};
                                if(negative=="no-original-failure")frame.Proposals[row].StaticNoWinner=false;if(negative=="unaligned")frame.Proposals[row].Heading+=actor.TurnSpeed/30;if(negative=="no-route")frame.RecastRoutes[row]=null;
                                var expectedBodies=(NavMotionBody[])frame.Motion.Clone();var expectedProposals=(NavMotionProposal[])frame.Proposals.Clone();Retry();TestContext.WriteLine("static-no-winner guard "+negative+" attempted="+NavCrowdMotion.Causal.RetryAttempted);Assert.False(NavCrowdMotion.Causal.RetryAttempted,negative);CollectionAssert.AreEqual(expectedBodies,frame.Motion,negative);CollectionAssert.AreEqual(expectedProposals,frame.Proposals,negative);
                            }finally{Reset();}
                        }
                        // Measure denial against the completed new-branch setup,
                        // including its successful guard scratch allocation.
                        physical=Budget(s).PhysicalBytes;held=Budget(s).HeldBytes;scopes=Budget(s).ActiveScopes;denials=0;deniedBytes=0;guardFlagsTemporary=false;
                        try{Budget(s).AllocationAdmission=bytes=>{denials++;deniedBytes=bytes;guardFlagsTemporary=!frame.Motion[row].Active&&!frame.Motion[peer].Active;return false;};var error=Assert.Throws<System.Reflection.TargetInvocationException>(Retry);TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="static-no-winner-typed-denial-before-assert",Error=error.InnerException.Message,denials,deniedBytes,guardFlagsTemporary,NavCrowdMotion.Causal.RetryAttempted,NavCrowdMotion.Causal.RetryAccepted,RetrySelected=Proposal(NavCrowdMotion.Causal.RetrySelected),PeerActive=frame.Motion[peer].Active}));Assert.That(error.InnerException,Is.TypeOf<InvalidOperationException>());Assert.AreEqual("Scratch exceeds prepared reservation.",error.InnerException.Message);}
                        finally{Budget(s).AllocationAdmission=null;}
                        Assert.True(guardFlagsTemporary);Assert.True(NavCrowdMotion.Causal.RetryAttempted);Assert.False(NavCrowdMotion.Causal.RetryAccepted);Assert.AreEqual(selected,NavCrowdMotion.Causal.RetrySelected);Assert.AreEqual(1,denials);Assert.Greater(deniedBytes,NavStorageScratch.LeaseBytes);
                        CollectionAssert.AreEqual(bodies,frame.Motion);CollectionAssert.AreEqual(proposals,frame.Proposals);CollectionAssert.AreEqual(hints,frame.Formation);for(int i=0;i<headers.Length;i++)Assert.True(frame.After[i].SameCaptureHeader(headers[i]));Assert.AreEqual(physical,Budget(s).PhysicalBytes);Assert.AreEqual(held,Budget(s).HeldBytes);Assert.AreEqual(scopes,Budget(s).ActiveScopes);Assert.AreEqual(publications,s.OwnedMovementPublications);CollectionAssert.AreEqual(before,Bytes());
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="actual-stationary-retry-controls",Result="PASS",OwnIncluded=true,NormalAndPolicyCompatibility=true,NegativeGuards=14,denials,deniedBytes,SourceBytes=before.Length,FullSourceExact=true,AllActiveRestored=true,NoPub=true,SessionSteps=0}));
                    }finally{frame.Release();}scope.Discard();}
                CollectionAssert.AreEqual(before,Bytes());Assert.AreEqual(publications,s.OwnedMovementPublications);
            }finally{NavCrowdMotion.CausalActor=savedActor;NavCrowdMotion.Causal=savedCausal;s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(Budget(s).PhysicalBytes);Assert.Zero(Budget(s).HeldBytes);Assert.Zero(Budget(s).ActiveScopes);Assert.AreEqual(native,RecastNative.sw_recast_allocation_count());Assert.AreEqual(faults,RecastNative.ReleaseFaults);}
        }
        [TestCase(false)][TestCase(true)] public void PeerClearPersistentLocalRejoinTurnsThenTranslatesAroundFixedRampPeer(bool forecastAhead)
        {
            ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;var s=Ready();
            byte[] Bytes(){using var capture=s.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,capture));}
            try{
                byte[] source=Bytes();int publications=s.OwnedMovementPublications;var route=Route(s);var map=PlayableProfile.RecastGroupCorridorDefault.AuthoredMap;
                Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,r);NavLocalRepairWorkspace scratch=null;
                    try{
                        frame.Observe();int row=frame.Find(2),peer=frame.Find(1);var actor=frame.After[row];var receiver=frame.After[peer];
                        var start=At(-30,42.73,"ramp/-1/1/x");var peerAt=At(-31.30,43.43,"ramp/-1/1/x");
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="fixed-peer-clear-ramp-setup",Start=start,Peer=peerAt,Radius=actor.Radius,Horizon=600,Dt=1d/30,Global=actor.route}));
                        Assert.True(map.IsValidLocation(start,actor.Radius));Assert.True(map.IsValidLocation(peerAt,receiver.Radius));
                        Assert.True(NavRecastIntervals.Advance(route,NavRecastIntervals.Initial(route),actor.Location,start.Position,r,out var initial,frame.Working,actor.Id));
                        actor.Location=start;actor.Position=start.Position;actor.routeIndex=NavRecastIntervals.FirstTargetSurface(route,initial.After);actor.localRoute=Array.Empty<NavPoint>();actor.localIndex=0;actor.noProgressTicks=10;actor.blockedSeconds=10d/30;
                        receiver.Location=peerAt;receiver.Position=peerAt.Position;receiver.Held=true;receiver.Moving=false;receiver.Outcome=NavigationOutcome.Held;
                        frame.BindRecast(row,route,initial.After);
                        Assert.AreEqual(4,actor.routeIndex,"Recorded earliest interval point is a free eastward control, not the blocked motor target");
                        double PeerDistanceSquared(NavPoint from,NavPoint to){double x=to.X-from.X,z=to.Z-from.Z,len=x*x+z*z;double t=len==0?0:Math.Max(0,Math.Min(1,((receiver.Position.X-from.X)*x+(receiver.Position.Z-from.Z)*z)/len));double dx=receiver.Position.X-from.X-t*x,dz=receiver.Position.Z-from.Z-t*z;return dx*dx+dz*dz;}
                        double radiusSquared=Math.Pow(actor.Radius+receiver.Radius,2);Assert.GreaterOrEqual(PeerDistanceSquared(actor.Position,actor.route[4]),radiusSquared);
                        Assert.Less(PeerDistanceSquared(actor.route[4],actor.route[5]),radiusSquared,"The canonical held tail excludes connector4 even though the current connector is free");
                        bool freeControl=NavRecastIntervals.TryContinuationRejoin(route,initial.After,actor.Location,Array.Empty<NavPoint>(),0,actor.route,4,actor.GoalLocation.Value,r,out int freeIndex,frame.Working,actor.Id);Assert.False(freeControl&&freeIndex==4);
                        bool targetLegal=NavRecastIntervals.CanTarget(route,initial.After,actor.Location,5,r)&&NavRecastIntervals.Advance(route,initial.After,actor.Location,actor.route[5],r,out _);
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="complete-candidate-legal-blocked-target-before-assert",TargetIndex=5,Target=actor.route[5],targetLegal,PeerDistanceSquared=PeerDistanceSquared(actor.Position,actor.route[5]),radiusSquared,freeControl,freeIndex}));Assert.True(targetLegal,"Existing accepted index5 must be legal; no alternate index/geometry search");Assert.Less(PeerDistanceSquared(actor.Position,actor.route[5]),radiusSquared);
                        var binding=route.Subscription;Assert.True(binding.Provenance==NavRouteProvenance.CommandMember&&binding.EntityId==actor.Id&&binding.Incarnation==actor.Incarnation&&binding.MobilityRevision==actor.MobilityRevision&&binding.RequestSequence==actor.InstalledRequest&&binding.GroupId==actor.InstalledGroupId&&binding.ActivationRevision==actor.InstalledRevision&&binding.MemberOrderRevision==actor.InstalledRevision&&binding.AssignedEndpoint.Equals(actor.GoalLocation.Value));
                        Assert.False(NavRecastIntervals.TryContinuationRejoin(route,initial.After,actor.Location,new[]{peerAt.Position},0,actor.route,5,actor.GoalLocation.Value,r,out _,frame.Working,actor.Id),"A physically invalid complete prefix cannot be a repair goal");
                        int forecastIndex=actor.route.Length-1;
                        if(forecastAhead){bool forecastTargetable=NavRecastIntervals.CanTarget(route,initial.After,actor.Location,forecastIndex,r);TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="forecast-range-precondition",forecastIndex,PhysicalFirst=NavRecastIntervals.FirstTargetSurface(route,initial.After),forecastTargetable,GuideTarget=actor.route[5]}));Assert.AreNotEqual(NavRecastIntervals.FirstTargetSurface(route,initial.After),forecastIndex);Assert.False(forecastTargetable,"One predeclared forecast index must be outside current physical targetability");}
                        actor.routeIndex=forecastAhead?forecastIndex:5;actor.Heading=Math.Atan2(actor.route[5].Z-start.Position.Z,actor.route[5].X-start.Position.X);bool initialGuide=true;
                        int portal=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);Assert.True(RecastFormationMotion.Axis(route,portal,out var origin,out _,out var axis,out _));
                        void Hint()=>frame.Formation[row]=new NavFormationFrameHint{Flags=NavFormationFrameHint.Guide|NavFormationFrameHint.Bound,Target=actor.route[initialGuide?5:Math.Min(actor.routeIndex,actor.route.Length-1)],TargetLeg=portal,ForwardLimit=RecastFormationMotion.Station(actor.Position,origin,axis)+frame.Profile.ArrivalSlotSpacing};
                        Hint();var build=typeof(NavCrowd).GetMethod("BuildIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);build.Invoke(frame.Working,null);
                        var repair=typeof(NavCrowd).GetMethod("CommitDetour",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                        bool Repair(){object[] args={actor,actor.route[actor.routeIndex],scratch};try{return (bool)repair.Invoke(frame.Working,args);}finally{scratch=(NavLocalRepairWorkspace)args[2];}}
                        var header=new NavUnitSaveState();actor.CopyCaptureHeaderTo(header);var oldLocal=actor.localRoute;int oldIndex=actor.routeIndex;
                        foreach(string negative in new[]{"held","changed-binding","no-suffix","enclosed","denied-scratch"}){
                            actor.Held=negative=="held";long request=actor.InstalledRequest;var global=actor.route;var peerLocation=receiver.Location;
                            try{
                                if(negative=="changed-binding")actor.InstalledRequest++;
                                if(negative=="no-suffix"){actor.route=(NavPoint[])global.Clone();for(int n=oldIndex;n<actor.route.Length;n++)actor.route[n]=peerAt.Position;}
                                if(negative=="enclosed"){receiver.Position=actor.Position;receiver.Location=actor.Location;build.Invoke(frame.Working,null);}
                                if(negative=="denied-scratch")Budget(s).AllocationAdmission=bytes=>false;
                                if(negative=="denied-scratch"){var error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>Repair());TestContext.WriteLine("peer-clear denied scratch: "+error.InnerException.Message);Assert.That(error.InnerException,Is.TypeOf<InvalidOperationException>());}
                                else{bool accepted=Repair();TestContext.WriteLine("peer-clear negative "+negative+" accepted="+accepted);Assert.False(accepted,negative);}
                                Budget(s).AllocationAdmission=null; // The subsequent source capture has its own admission.
                                Assert.AreSame(oldLocal,actor.localRoute);Assert.AreEqual(oldIndex,actor.routeIndex);Assert.AreEqual(10,actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds);Assert.AreEqual(publications,s.OwnedMovementPublications);CollectionAssert.AreEqual(source,Bytes());
                            }finally{Budget(s).AllocationAdmission=null;actor.Held=false;actor.InstalledRequest=request;actor.route=global;receiver.Position=peerAt.Position;receiver.Location=peerLocation;scratch?.ReleaseOwner();scratch=null;build.Invoke(frame.Working,null);}
                        }
                        Assert.True(actor.SameCaptureHeader(header));var typedBefore=frame.RecastStates[row];bool installed=Repair();TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="peer-clear-prefix-before-assert",forecastAhead,Installed=installed,Local=actor.localRoute,actor.routeIndex,actor.noProgressTicks,actor.blockedSeconds,RepairCpuMs=frame.Working.RepairCpuMs,Physical=Budget(s).PhysicalBytes,Held=Budget(s).HeldBytes,Scopes=Budget(s).ActiveScopes}));Assert.True(installed);Assert.AreEqual(typedBefore,frame.RecastStates[row],"Physical rejoin must not reset typed state");if(forecastAhead)Assert.Less(actor.routeIndex,forecastIndex);initialGuide=false;Assert.GreaterOrEqual(actor.localRoute.Length,2);Assert.Greater(Math.Sqrt(Math.Pow(actor.localRoute[0].X-actor.Position.X,2)+Math.Pow(actor.localRoute[0].Z-actor.Position.Z,2)),frame.Profile.ArrivalTolerance);
                        var prefix=actor.localRoute;int repairs=frame.Working.RepairCount;double gx=actor.Goal.X-start.Position.X,gz=actor.Goal.Z-start.Position.Z;Assert.Less((prefix[0].X-start.Position.X)*gx+(prefix[0].Z-start.Position.Z)*gz,0,"The initial bypass must move away from final goal");
                        Assert.True(NavRecastIntervals.TryContinuationRejoin(route,initial.After,actor.Location,prefix,0,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out int rejoin,frame.Working,actor.Id));Assert.AreEqual(actor.routeIndex,rejoin);
                        bool translated=false,turned=false,rejoined=false;double firstAim=Math.Atan2(prefix[0].Z-actor.Position.Z,prefix[0].X-actor.Position.X);double errorBefore=Math.Abs(NavCrowdMotion.Angle(firstAim-actor.Heading));int steps=0;
                        for(;steps<600&&actor.Moving;steps++){
                            var previous=actor.Location;double previousHeading=actor.Heading;Hint();frame.Compute(1d/30);var receipt=frame.RecastReceipts[row];
                            if(receipt.Verified)frame.RecastStates[row]=receipt.After;
                            Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(actor.Heading-previousHeading)),actor.TurnSpeed/30+1e-12);Assert.GreaterOrEqual(Math.Pow(actor.Position.X-receiver.Position.X,2)+Math.Pow(actor.Position.Z-receiver.Position.Z,2),Math.Pow(actor.Radius+receiver.Radius,2)-1e-9);Assert.True(map.IsValidLocation(actor.Location,actor.Radius));
                            if(!translated){
                                Assert.AreSame(prefix,actor.localRoute,"A stationary turn must retain its local aim");Assert.AreEqual(repairs,frame.Working.RepairCount,"No route reinstall without actual translation");
                                double errorNow=Math.Abs(NavCrowdMotion.Angle(firstAim-actor.Heading));Assert.LessOrEqual(errorNow,errorBefore+1e-12);errorBefore=errorNow;
                                if(!actor.Position.Equals(previous.Position)){translated=true;Assert.True(turned);TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="peer-clear-first-translation",Step=steps,From=previous,To=actor.Location,actor.Heading,actor.noProgressTicks}));Assert.True(receipt.Verified);Assert.Zero(actor.noProgressTicks);}
                                else{turned|=actor.Heading!=previousHeading;Assert.AreEqual(10,actor.noProgressTicks,"A stationary local turn must not reset the counter");}
                            }
                            rejoined|=actor.localIndex>=prefix.Length;Assert.AreEqual(publications,s.OwnedMovementPublications);CollectionAssert.AreEqual(source,Bytes());
                        }
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="peer-clear-final-before-assert",translated,turned,rejoined,steps,actor.Position,actor.Outcome,actor.routeIndex,actor.localIndex,Repairs=frame.Working.RepairCount}));Assert.True(translated);Assert.True(rejoined);Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome);Assert.True(frame.Working.CompatibleArrival(actor.Location,actor.GoalLocation.Value,actor.Radius));
                    }finally{scratch?.ReleaseOwner();frame.Release();}scope.Discard();}
                CollectionAssert.AreEqual(source,Bytes());Assert.AreEqual(publications,s.OwnedMovementPublications);
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(Budget(s).PhysicalBytes);Assert.Zero(Budget(s).HeldBytes);Assert.Zero(Budget(s).ActiveScopes);Assert.AreEqual(native,RecastNative.sw_recast_allocation_count());Assert.AreEqual(faults,RecastNative.ReleaseFaults);}
        }
        [TestCase(false)]
        [TestCase(true)]
        public void TypedFilterDistinguishesRejectedTranslationFromAnEntryGate(bool preEntry)
        {
            var session=Ready();
            var budget=Budget(session);
            try
            {
                var route=Route(session);
                int entry=Enumerable.Range(0,route.Legs.Count).First(i=>route.Legs[i].Kind==NavTypedLegKind.Portal);
                var from=At(-29.49991393925073,45.58051079028784,"bank/west");
                var crossing=new NavPoint(-29.51317328835296,45.57291391940347);
                var state=new NavRecastStepState(1,route.RecastPlan.OriginLegs+1,1.7,2.2);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope)
                {
                    var frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,scope.OwnedReservation());
                    try
                    {
                        frame.Observe();
                        int row=frame.Find(2),peer=frame.Find(1);
                        var actor=frame.After[row];
                        actor.Location=new NavLocation(from.Position,actor.Location.NameReference);
                        actor.Position=from.Position;
                        var body=new NavMotionBody{Id=actor.Id,Position=from.Position,Active=true,OwnMoving=true};
                        frame.Motion[row]=body;
                        frame.Motion[peer]=new NavMotionBody{Id=1,Position=frame.After[peer].Position};
                        frame.Proposals[peer]=new NavMotionProposal{Position=frame.After[peer].Position};
                        frame.BindRecast(row,route,state);
                        var step=crossing;
                        if(preEntry)
                            frame.Formation[row]=new NavFormationFrameHint{Flags=NavFormationFrameHint.PreEntry,TargetLeg=entry};
                        else
                        {
                            // A pending formation guide may offer a physically safe step
                            // while no accepted motor suffix remains after the step.
                            frame.Formation[row]=new NavFormationFrameHint{Flags=NavFormationFrameHint.Guide};
                            actor.route=new[]{new NavPoint(10000,10000)};
                            actor.routeIndex=0;
                        }
                        Assert.True(NavRecastIntervals.Advance(route,state,actor.Location,step,frame.Reservation,
                            out _,frame.Working,actor.Id),"The raw typed crossing is valid.");
                        frame.Proposals[row]=new NavMotionProposal{Position=step,
                            Velocity=new NavPoint(30*(step.X-from.Position.X),30*(step.Z-from.Position.Z))};
                        typeof(NavCrowd).GetMethod("FilterRecastSweeps",
                            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                            .Invoke(frame.Working,new object[]{1d/30,frame.Motion,frame.Proposals});
                        Assert.AreEqual(from.Position,frame.Proposals[row].Position);
                        Assert.True(frame.RecastReceipts[row].Verified);
                        Assert.AreEqual(from.Position,frame.RecastReceipts[row].To);
                        // The pre-entry denial remains policy, while a refused
                        // physical continuation can use bounded stall recovery.
                        Assert.AreEqual(!preEntry,frame.Proposals[row].TypedTranslationRefused);
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

        [TestCase(false)]
        [TestCase(true)]
        public void MissingTypedMotorTargetUsesBlockedClockButVoluntaryWaitDoesNot(bool wait)
        {
            var session=Ready();
            var budget=Budget(session);
            const double dt=1d/30;
            try
            {
                var route=Route(session);
                var state=NavRecastIntervals.Initial(route);
                var position=new NavPoint(-24.75,48);
                for(int i=0;i<8;i++)
                {
                    double angle=i*Math.PI/4;
                    var point=new NavPoint(position.X+.95*Math.Cos(angle),position.Z+.95*Math.Sin(angle));
                    var blocker=session.Crowd.Add(101+i,point,.15,4,4.5);
                    blocker.Held=true;
                }
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));
                using(scope)
                {
                    var frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,scope.OwnedReservation());
                    try
                    {
                        frame.Observe();
                        int row=frame.Find(2);
                        var actor=frame.After[row];
                        var origin=new NavLocation(position,actor.Location.NameReference);
                        actor.Position=position;
                        actor.Location=origin;
                        actor.routeIndex=NavRecastIntervals.FirstTargetSurface(route,state);
                        actor.noProgressTicks=(int)Math.Ceiling(frame.Profile.BlockedTimeoutSeconds/dt)-1;
                        int prior=actor.noProgressTicks;
                        frame.BindRecast(row,route,state);
                        Assert.True(frame.Working.ValidLocation(origin,actor.Radius));
                        for(int i=actor.routeIndex;i<actor.route.Length;i++)
                            Assert.False(NavRecastIntervals.CanTarget(route,state,origin,i,frame.Reservation,frame.Working,actor.Id),
                                "Held footprints must cover every eligible typed target from the actual pose.");
                        Assert.True(NavRecastIntervals.Advance(route,state,origin,origin.Position,frame.Reservation,out _,frame.Working,actor.Id));
                        if(wait)
                        {
                            frame.Preferred[row]=0;
                            frame.Formation[row]=new NavFormationFrameHint{Flags=NavFormationFrameHint.Wait};
                        }

                        frame.Compute(dt);

                        Assert.False(frame.Motion[row].Active);
                        Assert.AreEqual(origin,actor.Location);
                        Assert.AreEqual(wait?prior:0,actor.noProgressTicks);
                        Assert.AreEqual(wait?NavigationOutcome.Moving:NavigationOutcome.Blocked,actor.Outcome);
                        Assert.AreEqual(wait,actor.Moving);
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

        [Test]
        public void RecordedAntennaRepairMustLeaveTheProducedPeerPocket()
        {
            var map=new AntennaCityMap();
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            const double radius=.58;
            var buildings=new (PlayableBuildingKind,NavPoint)[]{
                (PlayableBuildingKind.Headquarters,new NavPoint(60.000000000000014,103.92304845413263)),
                (PlayableBuildingKind.Headquarters,new NavPoint(120.0,0.0)),
                (PlayableBuildingKind.Headquarters,new NavPoint(60.000000000000014,-103.92304845413263)),
                (PlayableBuildingKind.Headquarters,new NavPoint(-59.99999999999998,-103.92304845413264)),
                (PlayableBuildingKind.Headquarters,new NavPoint(-59.99999999999998,103.92304845413264)),
                (PlayableBuildingKind.Headquarters,new NavPoint(-120.0,-1.469576158976824e-14)),
                (PlayableBuildingKind.Refinery,new NavPoint(-53.49999999999998,103.92304845413264)),
                (PlayableBuildingKind.Refinery,new NavPoint(-113.5,-1.469576158976824e-14)),
                (PlayableBuildingKind.Refinery,new NavPoint(66.50000000000001,103.92304845413263)),
                (PlayableBuildingKind.Refinery,new NavPoint(126.5,0.0)),
                (PlayableBuildingKind.Refinery,new NavPoint(66.50000000000001,-103.92304845413263)),
                (PlayableBuildingKind.Refinery,new NavPoint(-53.49999999999998,-103.92304845413264)),
                (PlayableBuildingKind.Factory,new NavPoint(62.00861046343717,110.10491581005112)),
                (PlayableBuildingKind.Factory,new NavPoint(-57.99138953656282,110.10491581005114)),
                (PlayableBuildingKind.Refinery,new NavPoint(62.00861046343717,-97.74118109821413)),
                (PlayableBuildingKind.Refinery,new NavPoint(-117.99138953656283,6.181867355918483)),
                (PlayableBuildingKind.Refinery,new NavPoint(122.00861046343717,6.181867355918498)),
                (PlayableBuildingKind.Factory,new NavPoint(-57.99138953656282,-97.74118109821414)),
                (PlayableBuildingKind.Refinery,new NavPoint(114.74138953656285,3.820604139901076)),
                (PlayableBuildingKind.Factory,new NavPoint(54.741389536562856,-100.10244431423155)),
                (PlayableBuildingKind.Factory,new NavPoint(-125.25861046343715,3.8206041399010613)),
                (PlayableBuildingKind.Factory,new NavPoint(114.74138953656285,-3.8206041399010746)),
                (PlayableBuildingKind.Refinery,new NavPoint(54.741389536562856,107.7436525940337)),
                (PlayableBuildingKind.Refinery,new NavPoint(54.741389536562856,100.10244431423155)),
                (PlayableBuildingKind.Refinery,new NavPoint(54.741389536562856,-107.7436525940337)),
                (PlayableBuildingKind.Refinery,new NavPoint(-65.25861046343714,-100.10244431423156)),
                (PlayableBuildingKind.Hangar,new NavPoint(62.00861046343717,-110.10491581005112)),
                (PlayableBuildingKind.Refinery,new NavPoint(-65.25861046343714,107.74365259403372)),
                (PlayableBuildingKind.Outpost,new NavPoint(-40.999999999999986,71.01408311032398)),
                (PlayableBuildingKind.Refinery,new NavPoint(-65.25861046343714,100.10244431423156)),
                (PlayableBuildingKind.Refinery,new NavPoint(-65.25861046343714,-107.74365259403372)),
                (PlayableBuildingKind.Mine,new NavPoint(-70.99999999999997,122.9756073373903)),
                (PlayableBuildingKind.Refinery,new NavPoint(-125.25861046343715,-3.8206041399010893)),
                (PlayableBuildingKind.Refinery,new NavPoint(-34.499999999999986,71.01408311032398)),
                (PlayableBuildingKind.Outpost,new NavPoint(-82.0,-1.0042103753008296e-14)),
                (PlayableBuildingKind.RepairTower,new NavPoint(-68.49999999999997,-103.92304845413264)),
                (PlayableBuildingKind.RepairTower,new NavPoint(-62.62664445218703,112.00702884264145)),
                (PlayableBuildingKind.Outpost,new NavPoint(41.00000000000001,-71.01408311032397)),
                (PlayableBuildingKind.Hangar,new NavPoint(-75.5,-1.0042103753008296e-14)),
                (PlayableBuildingKind.Outpost,new NavPoint(82.0,0.0)),
                (PlayableBuildingKind.Refinery,new NavPoint(-44.249999999999986,65.38491798572514)),
                (PlayableBuildingKind.SentryTurret,new NavPoint(-36.749999999999986,78.3752990424917)),
                (PlayableBuildingKind.Refinery,new NavPoint(88.5,0.0)),
                (PlayableBuildingKind.Refinery,new NavPoint(-85.25,5.629165124598842)),
                (PlayableBuildingKind.ScientificCenter,new NavPoint(62.00861046343717,97.74118109821413)),
                (PlayableBuildingKind.Refinery,new NavPoint(78.75,5.629165124598852)),
                (PlayableBuildingKind.Mine,new NavPoint(71.00000000000001,-122.97560733739029)),
                (PlayableBuildingKind.Refinery,new NavPoint(47.50000000000001,-71.01408311032397)),
                (PlayableBuildingKind.Refinery,new NavPoint(-85.25,-5.629165124598859)),
                (PlayableBuildingKind.Refinery,new NavPoint(37.75000000000001,-65.38491798572511)),
                (PlayableBuildingKind.SentryTurret,new NavPoint(-77.75,-7.361215932167739)),
                (PlayableBuildingKind.Outpost,new NavPoint(-40.999999999999986,-71.01408311032398)),
                (PlayableBuildingKind.ScientificCenter,new NavPoint(-117.99138953656285,-6.181867355918514))
            };
            var obstacles=map.MovementBlockers.Concat(buildings.Select(b=>{
                double size=TerritoryRules.Radius(profile,b.Item1);
                return new NavObstacle(b.Item2.X-size,b.Item2.Z-size,b.Item2.X+size,b.Item2.Z+size);
            })).ToArray();
            var geometry=new NavGeometry(map.HalfExtent,obstacles,135);
            var session=new NavigationSession(1,geometry,profile.Navigation,null,map);
            var budget=session.PhysicalHostBudget;
            try
            {
                var start=new NavLocation(new NavPoint(-58.709311722631668,-99.789511018083417),"city/street");
                var goal=new NavLocation(new NavPoint(-34.90437817456598,-33.22557726028393),"city/plaza/2");
                var mover=session.Crowd.Add(155,start.Position,radius,PlayableUnitRules.Speed(profile,PlayableEntityKind.Explorer),
                    PlayableUnitRules.Turn(profile,PlayableEntityKind.Explorer),start);
                var peer=new NavLocation(new NavPoint(-57.99138953656282,-100.74118109821414),"city/street");
                session.Crowd.Add(169,peer.Position,radius,mover.Speed,mover.TurnSpeed,peer);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));
                using(scope)
                {
                    var reservation=scope.OwnedReservation();
                    var provider=PartitionNavigationProvider.CopyOwned(new PartitionNavigationProvider(map,geometry,""),reservation);
                    NavTypedLeg[] typed=null,shared=null;
                    NavRecastSpine spine=null;
                    NavRecastMemberPlan plan=null;
                    NavTypedRoute route=null;
                    NavPreparedCrowdStep frame=null;
                    try
                    {
                        var recorded=new (NavLocation From,NavLocation To,double Cost,string Portal)[]{
                            (new NavLocation(new NavPoint(-60.09138953656283,-100.74118109821414),"city/street"),new NavLocation(new NavPoint(-60.09138953656283,-100.74118109821414),"city/street"),0.0,null),
                (new NavLocation(new NavPoint(-60.09138953656283,-100.74118109821414),"city/street"),new NavLocation(new NavPoint(-60.09138870239258,-100.74118041992188),"city/street"),1.0751373898306163e-06,null),
                (new NavLocation(new NavPoint(-60.09138870239258,-100.74118041992188),"city/street"),new NavLocation(new NavPoint(-59.28141403198242,-100.55033874511719),"city/street"),0.8321535384460607,null),
                (new NavLocation(new NavPoint(-59.28141403198242,-100.55033874511719),"city/street"),new NavLocation(new NavPoint(-58.68359375,-100.40948486328125),"city/street"),0.6141896332386233,null),
                (new NavLocation(new NavPoint(-58.68359375,-100.40948486328125),"city/street"),new NavLocation(new NavPoint(-58.2542724609375,-100.30833435058594),"city/street"),0.4410761787501239,null),
                (new NavLocation(new NavPoint(-58.2542724609375,-100.30833435058594),"city/street"),new NavLocation(new NavPoint(-56.19999694824219,-99.8243179321289),"city/street"),2.110525947576945,null),
                (new NavLocation(new NavPoint(-56.19999694824219,-99.8243179321289),"city/street"),new NavLocation(new NavPoint(-55.79999923706055,-99.42431640625),"city/street"),0.5656848854760081,null),
                (new NavLocation(new NavPoint(-55.79999923706055,-99.42431640625),"city/street"),new NavLocation(new NavPoint(-54.79999923706055,-96.0243148803711),"city/street"),3.5440104932094783,null),
                (new NavLocation(new NavPoint(-54.79999923706055,-96.0243148803711),"city/street"),new NavLocation(new NavPoint(-45.78434753417969,-83.22431945800781),"city/street"),15.656367983672952,null),
                (new NavLocation(new NavPoint(-45.78434753417969,-83.22431945800781),"city/street"),new NavLocation(new NavPoint(-42.47887420654297,-78.53136444091797),"city/street"),5.740207375360797,null),
                (new NavLocation(new NavPoint(-42.47887420654297,-78.53136444091797),"city/street"),new NavLocation(new NavPoint(-42.0,-77.85148620605469),"city/street"),0.831597811404629,null),
                (new NavLocation(new NavPoint(-42.0,-77.85148620605469),"city/street"),new NavLocation(new NavPoint(-39.00712966918945,-73.60234832763672),"city/street"),5.197349856114402,null),
                (new NavLocation(new NavPoint(-39.00712966918945,-73.60234832763672),"city/street"),new NavLocation(new NavPoint(-38.984230041503906,-73.56983947753906),"city/street"),0.03976453549091891,null),
                (new NavLocation(new NavPoint(-38.984230041503906,-73.56983947753906),"city/street"),new NavLocation(new NavPoint(-38.599998474121094,-73.02432250976562),"city/street"),0.6672500726880278,null),
                (new NavLocation(new NavPoint(-38.599998474121094,-73.02432250976562),"city/street"),new NavLocation(new NavPoint(-37.48139190673828,-70.42431640625),"city/street"),2.830426185384537,null),
                (new NavLocation(new NavPoint(-37.48139190673828,-70.42431640625),"city/street"),new NavLocation(new NavPoint(-36.982696533203125,-69.26519012451172),"city/street"),1.2618521357915842,null),
                (new NavLocation(new NavPoint(-36.982696533203125,-69.26519012451172),"city/street"),new NavLocation(new NavPoint(-32.198795318603516,-58.14584732055664),"city/street"),12.104771589043521,null),
                (new NavLocation(new NavPoint(-32.198795318603516,-58.14584732055664),"city/street"),new NavLocation(new NavPoint(-31.97441864013672,-57.62431716918945),"city/street"),0.5677487055246131,null),
                (new NavLocation(new NavPoint(-31.97441864013672,-57.62431716918945),"city/street"),new NavLocation(new NavPoint(-31.788787841796875,-57.19285202026367),"city/street"),0.4697030636794211,null),
                (new NavLocation(new NavPoint(-31.788787841796875,-57.19285202026367),"city/street"),new NavLocation(new NavPoint(-31.37809181213379,-56.238258361816406),"city/street"),1.0391921292661706,null),
                (new NavLocation(new NavPoint(-31.37809181213379,-56.238258361816406),"city/street"),new NavLocation(new NavPoint(-31.36836591406832,-56.21565227104132),"city/street"),0.02460951916050506,null),
                (new NavLocation(new NavPoint(-31.36836591406832,-56.21565227104132),"city/street"),new NavLocation(new NavPoint(-31.36836591406832,-56.21565227104132),"city/ramp/2/-1"),0.0,"ca4e1cda0dbbab92178e0ed4768f86865bcef50f748d71e38349d97b8c8e4781"),
                (new NavLocation(new NavPoint(-31.36836591406832,-56.21565227104132),"city/ramp/2/-1"),new NavLocation(new NavPoint(-31.200000762939453,-55.824317932128906),"city/ramp/2/-1"),0.4260157144128175,null),
                (new NavLocation(new NavPoint(-31.200000762939453,-55.824317932128906),"city/ramp/2/-1"),new NavLocation(new NavPoint(-32.903770446777344,-45.430389404296875),"city/ramp/2/-1"),10.532643608194745,null),
                (new NavLocation(new NavPoint(-32.903770446777344,-45.430389404296875),"city/ramp/2/-1"),new NavLocation(new NavPoint(-33.003116607666016,-44.824317932128906),"city/ramp/2/-1"),0.6141598237097301,null),
                (new NavLocation(new NavPoint(-33.003116607666016,-44.824317932128906),"city/ramp/2/-1"),new NavLocation(new NavPoint(-33.92257372992244,-39.21511622702292),"city/ramp/2/-1"),5.684060623201691,null),
                (new NavLocation(new NavPoint(-33.92257372992244,-39.21511622702292),"city/ramp/2/-1"),new NavLocation(new NavPoint(-33.92257372992244,-39.21511622702292),"city/plaza/2"),0.0,"2390d3067391c5cdac0c53346a659858dee225b2d5e0b2eab54bfc03f0ef0281"),
                (new NavLocation(new NavPoint(-33.92257372992244,-39.21511622702292),"city/plaza/2"),new NavLocation(new NavPoint(-33.9734992980957,-38.904441833496094),"city/plaza/2"),0.31482057157534926,null),
                (new NavLocation(new NavPoint(-33.9734992980957,-38.904441833496094),"city/plaza/2"),new NavLocation(new NavPoint(-34.22821666750916,-37.350526425591866),"city/plaza/2"),1.5746536232464183,null),
                (new NavLocation(new NavPoint(-34.22821666750916,-37.350526425591866),"city/plaza/2"),new NavLocation(new NavPoint(-34.90437817456598,-33.22557726028393),"city/plaza/2"),4.180000000000001,null)
                        };
                        typed=recorded.Select(l=>NavTypedLeg.CreateOwned(l.From,l.To,l.Cost,
                            l.Portal==null?NavTypedLegKind.Surface:NavTypedLegKind.Portal,l.Portal,reservation,provider)).ToArray();
                        var recordedSpine=new (NavLocation From,NavLocation To,double Cost,string Portal)[]{
                            (new NavLocation(new NavPoint(-60.09138953656283,-100.74118109821414),"city/street"),new NavLocation(new NavPoint(-60.09138870239258,-100.74118041992188),"city/street"),1.0751373898306163e-06,null),
                (new NavLocation(new NavPoint(-60.09138870239258,-100.74118041992188),"city/street"),new NavLocation(new NavPoint(-59.28141403198242,-100.55033874511719),"city/street"),0.8321535384460607,null),
                (new NavLocation(new NavPoint(-59.28141403198242,-100.55033874511719),"city/street"),new NavLocation(new NavPoint(-58.68359375,-100.40948486328125),"city/street"),0.6141896332386233,null),
                (new NavLocation(new NavPoint(-58.68359375,-100.40948486328125),"city/street"),new NavLocation(new NavPoint(-58.2542724609375,-100.30833435058594),"city/street"),0.4410761787501239,null),
                (new NavLocation(new NavPoint(-58.2542724609375,-100.30833435058594),"city/street"),new NavLocation(new NavPoint(-56.19999694824219,-99.8243179321289),"city/street"),2.110525947576945,null),
                (new NavLocation(new NavPoint(-56.19999694824219,-99.8243179321289),"city/street"),new NavLocation(new NavPoint(-55.79999923706055,-99.42431640625),"city/street"),0.5656848854760081,null),
                (new NavLocation(new NavPoint(-55.79999923706055,-99.42431640625),"city/street"),new NavLocation(new NavPoint(-54.79999923706055,-96.0243148803711),"city/street"),3.5440104932094783,null),
                (new NavLocation(new NavPoint(-54.79999923706055,-96.0243148803711),"city/street"),new NavLocation(new NavPoint(-45.78434753417969,-83.22431945800781),"city/street"),15.656367983672952,null),
                (new NavLocation(new NavPoint(-45.78434753417969,-83.22431945800781),"city/street"),new NavLocation(new NavPoint(-42.47887420654297,-78.53136444091797),"city/street"),5.740207375360797,null),
                (new NavLocation(new NavPoint(-42.47887420654297,-78.53136444091797),"city/street"),new NavLocation(new NavPoint(-42.0,-77.85148620605469),"city/street"),0.831597811404629,null),
                (new NavLocation(new NavPoint(-42.0,-77.85148620605469),"city/street"),new NavLocation(new NavPoint(-39.00712966918945,-73.60234832763672),"city/street"),5.197349856114402,null),
                (new NavLocation(new NavPoint(-39.00712966918945,-73.60234832763672),"city/street"),new NavLocation(new NavPoint(-38.984230041503906,-73.56983947753906),"city/street"),0.03976453549091891,null),
                (new NavLocation(new NavPoint(-38.984230041503906,-73.56983947753906),"city/street"),new NavLocation(new NavPoint(-38.599998474121094,-73.02432250976562),"city/street"),0.6672500726880278,null),
                (new NavLocation(new NavPoint(-38.599998474121094,-73.02432250976562),"city/street"),new NavLocation(new NavPoint(-37.48139190673828,-70.42431640625),"city/street"),2.830426185384537,null),
                (new NavLocation(new NavPoint(-37.48139190673828,-70.42431640625),"city/street"),new NavLocation(new NavPoint(-36.982696533203125,-69.26519012451172),"city/street"),1.2618521357915842,null),
                (new NavLocation(new NavPoint(-36.982696533203125,-69.26519012451172),"city/street"),new NavLocation(new NavPoint(-32.198795318603516,-58.14584732055664),"city/street"),12.104771589043521,null),
                (new NavLocation(new NavPoint(-32.198795318603516,-58.14584732055664),"city/street"),new NavLocation(new NavPoint(-31.97441864013672,-57.62431716918945),"city/street"),0.5677487055246131,null),
                (new NavLocation(new NavPoint(-31.97441864013672,-57.62431716918945),"city/street"),new NavLocation(new NavPoint(-31.788787841796875,-57.19285202026367),"city/street"),0.4697030636794211,null),
                (new NavLocation(new NavPoint(-31.788787841796875,-57.19285202026367),"city/street"),new NavLocation(new NavPoint(-31.37809181213379,-56.238258361816406),"city/street"),1.0391921292661706,null),
                (new NavLocation(new NavPoint(-31.37809181213379,-56.238258361816406),"city/street"),new NavLocation(new NavPoint(-31.36836591406832,-56.21565227104132),"city/street"),0.02460951916050506,null),
                (new NavLocation(new NavPoint(-31.36836591406832,-56.21565227104132),"city/street"),new NavLocation(new NavPoint(-31.36836591406832,-56.21565227104132),"city/ramp/2/-1"),0.0,"ca4e1cda0dbbab92178e0ed4768f86865bcef50f748d71e38349d97b8c8e4781"),
                (new NavLocation(new NavPoint(-31.36836591406832,-56.21565227104132),"city/ramp/2/-1"),new NavLocation(new NavPoint(-31.200000762939453,-55.824317932128906),"city/ramp/2/-1"),0.4260157144128175,null),
                (new NavLocation(new NavPoint(-31.200000762939453,-55.824317932128906),"city/ramp/2/-1"),new NavLocation(new NavPoint(-32.903770446777344,-45.430389404296875),"city/ramp/2/-1"),10.532643608194745,null),
                (new NavLocation(new NavPoint(-32.903770446777344,-45.430389404296875),"city/ramp/2/-1"),new NavLocation(new NavPoint(-33.003116607666016,-44.824317932128906),"city/ramp/2/-1"),0.6141598237097301,null),
                (new NavLocation(new NavPoint(-33.003116607666016,-44.824317932128906),"city/ramp/2/-1"),new NavLocation(new NavPoint(-33.92257372992244,-39.21511622702292),"city/ramp/2/-1"),5.684060623201691,null),
                (new NavLocation(new NavPoint(-33.92257372992244,-39.21511622702292),"city/ramp/2/-1"),new NavLocation(new NavPoint(-33.92257372992244,-39.21511622702292),"city/plaza/2"),0.0,"2390d3067391c5cdac0c53346a659858dee225b2d5e0b2eab54bfc03f0ef0281"),
                (new NavLocation(new NavPoint(-33.92257372992244,-39.21511622702292),"city/plaza/2"),new NavLocation(new NavPoint(-33.9734992980957,-38.904441833496094),"city/plaza/2"),0.31482057157534926,null),
                (new NavLocation(new NavPoint(-33.9734992980957,-38.904441833496094),"city/plaza/2"),new NavLocation(new NavPoint(-34.22821666750916,-37.350526425591866),"city/plaza/2"),1.5746536232464183,null),
                (new NavLocation(new NavPoint(-34.22821666750916,-37.350526425591866),"city/plaza/2"),new NavLocation(new NavPoint(-34.90437698364258,-33.22557830810547),"city/plaza/2"),4.179998773333339,null),
                (new NavLocation(new NavPoint(-34.90437698364258,-33.22557830810547),"city/plaza/2"),new NavLocation(new NavPoint(-34.90437817456598,-33.22557726028393),"city/plaza/2"),1.5862624389445543e-06,null)
                        };
                        shared=recordedSpine.Select(l=>NavTypedLeg.CreateOwned(l.From,l.To,l.Cost,
                            l.Portal==null?NavTypedLegKind.Surface:NavTypedLegKind.Portal,l.Portal,reservation,provider)).ToArray();
                        var graph=new string('a',64);
                        spine=NavRecastSpine.Create(1,114,115,2305843009213694242,radius,graph,graph,graph,
                            provider.Binding,graph,"",geometry.Revision,new NavPoint(-34.90437817456598,-33.22557726028393),4.1800000000000015,28,shared,reservation);
                        plan=NavRecastMemberPlan.Create(spine,0,28,1,29,graph,graph,reservation);
                        var subscription=new NavRouteSubscription(1,114,115,2305843009213694242,15551,115,155,mover.Incarnation,115,
                            mover.MobilityRevision,412,recorded[0].From,goal,"");
                        route=NavTypedRoute.CopySubscriptionAndCreateOwned(subscription,typed,typed.Sum(l=>l.Cost),reservation,plan);
                        frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,reservation);
                        frame.Observe();
                        int row=frame.Find(155);
                        var actor=frame.After[row];
                        actor.Heading=-0.013870193685676352;
                        actor.route=typed.Where(l=>l.Kind==NavTypedLegKind.Surface).Select(l=>l.To.Position).ToArray();
                        actor.routeIndex=5;
                        actor.Moving=true;
                        actor.Outcome=NavigationOutcome.Moving;
                        actor.Goal=goal.Position;
                        actor.GoalLocation=goal;
                        actor.InstalledRequest=412;
                        actor.InstalledOrder=2;
                        actor.InstalledGroupId=114;
                        actor.InstalledRevision=115;
                        frame.BindRecast(row,route,new NavRecastStepState(1,4,1.8874204255721976,1.8874204255721976));
                        frame.Formation[row]=new NavFormationFrameHint{Flags=NavFormationFrameHint.Bound,TargetLeg=21,ForwardLimit=-.58};
                        int repairs=0;
                        double separation=0;
                        for(int tick=0;tick<300;tick++)
                        {
                            frame.Compute(1d/30);
                            if(frame.RecastReceipts[row].Verified)frame.RecastStates[row]=frame.RecastReceipts[row].After;
                            separation=Math.Max(separation,LayeredNavigationProvider.Distance(start.Position,actor.Position));
                            if(repairs!=frame.Working.RepairCount||tick%30==0||tick==299)
                            {
                                repairs=frame.Working.RepairCount;
                                TestContext.WriteLine($"ANTENNA_POCKET tick={tick} x={actor.Position.X:R} z={actor.Position.Z:R} separation={separation:R} ix={actor.routeIndex} li={actor.localIndex} local={string.Join(" ",actor.localRoute.Select(p=>$"{p.X:R},{p.Z:R}"))} repairs={repairs} outcome={actor.Outcome}");
                            }
                            if(separation>5)break;
                        }
                        Assert.Greater(separation,5,"The original route must leave the pocket while the new peer remains stationary.");
                        Assert.AreEqual(412,actor.InstalledRequest);
                    }
                    finally
                    {
                        frame?.Release();route?.ReleaseOwner();plan?.Release();spine?.Release();
                        if(typed!=null)foreach(var leg in typed)leg.ReleaseOwner();
                        if(shared!=null)foreach(var leg in shared)leg.ReleaseOwner();
                        ((INavStorageFacade)provider).StorageRoot.Release();
                    }
                    scope.Discard();
                }
            }
            finally
            {
                session.DestroyNavigationAuthority();
                Assert.Zero(budget.PhysicalBytes);
                Assert.Zero(budget.HeldBytes);
            }
        }

        [Test]
        public void RecordedAntennaMotorFromFactoryPocket()
        {
            var map=new AntennaCityMap();
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            const double radius=.58;
            var buildings=new (PlayableBuildingKind,NavPoint)[]{
                (PlayableBuildingKind.Headquarters,new NavPoint(60.000000000000014,103.92304845413263)),
                (PlayableBuildingKind.Headquarters,new NavPoint(120.0,0.0)),
                (PlayableBuildingKind.Headquarters,new NavPoint(60.000000000000014,-103.92304845413263)),
                (PlayableBuildingKind.Headquarters,new NavPoint(-59.99999999999998,-103.92304845413264)),
                (PlayableBuildingKind.Headquarters,new NavPoint(-59.99999999999998,103.92304845413264)),
                (PlayableBuildingKind.Headquarters,new NavPoint(-120.0,-1.469576158976824e-14)),
                (PlayableBuildingKind.Refinery,new NavPoint(66.50000000000001,-103.92304845413263)),
                (PlayableBuildingKind.Refinery,new NavPoint(-53.49999999999998,-103.92304845413264)),
                (PlayableBuildingKind.Refinery,new NavPoint(-53.49999999999998,103.92304845413264)),
                (PlayableBuildingKind.Refinery,new NavPoint(-113.5,-1.469576158976824e-14)),
                (PlayableBuildingKind.Refinery,new NavPoint(66.50000000000001,103.92304845413263)),
                (PlayableBuildingKind.Refinery,new NavPoint(126.5,0.0)),
                (PlayableBuildingKind.Refinery,new NavPoint(62.00861046343717,-97.74118109821413)),
                (PlayableBuildingKind.Factory,new NavPoint(-57.99138953656282,110.10491581005114)),
                (PlayableBuildingKind.Refinery,new NavPoint(-117.99138953656283,6.181867355918483)),
                (PlayableBuildingKind.Factory,new NavPoint(-57.99138953656282,-97.74118109821414)),
                (PlayableBuildingKind.Factory,new NavPoint(62.00861046343717,110.10491581005112)),
                (PlayableBuildingKind.Refinery,new NavPoint(122.00861046343717,6.181867355918498)),
                (PlayableBuildingKind.Refinery,new NavPoint(114.74138953656285,3.820604139901076)),
                (PlayableBuildingKind.Factory,new NavPoint(-125.25861046343715,3.8206041399010613)),
                (PlayableBuildingKind.Factory,new NavPoint(54.741389536562856,-100.10244431423155)),
                (PlayableBuildingKind.Factory,new NavPoint(114.74138953656285,-3.8206041399010746)),
                (PlayableBuildingKind.Refinery,new NavPoint(54.741389536562856,107.7436525940337)),
                (PlayableBuildingKind.Refinery,new NavPoint(-65.25861046343714,107.74365259403372)),
                (PlayableBuildingKind.Refinery,new NavPoint(-65.25861046343714,100.10244431423156)),
                (PlayableBuildingKind.Outpost,new NavPoint(82.0,0.0)),
                (PlayableBuildingKind.Mine,new NavPoint(-142.0,-1.7389984547892416e-14)),
                (PlayableBuildingKind.Refinery,new NavPoint(-65.25861046343714,-100.10244431423156)),
                (PlayableBuildingKind.Outpost,new NavPoint(-82.0,-1.0042103753008296e-14)),
                (PlayableBuildingKind.Refinery,new NavPoint(54.741389536562856,-107.7436525940337)),
                (PlayableBuildingKind.Outpost,new NavPoint(-40.999999999999986,-71.01408311032398)),
                (PlayableBuildingKind.Outpost,new NavPoint(41.00000000000001,71.01408311032397)),
                (PlayableBuildingKind.Outpost,new NavPoint(-40.999999999999986,71.01408311032398)),
                (PlayableBuildingKind.Hangar,new NavPoint(-75.5,-1.0042103753008296e-14)),
                (PlayableBuildingKind.Hangar,new NavPoint(62.00861046343717,-110.10491581005112)),
                (PlayableBuildingKind.Refinery,new NavPoint(-34.499999999999986,-71.01408311032398)),
                (PlayableBuildingKind.Refinery,new NavPoint(-85.25,5.629165124598842)),
                (PlayableBuildingKind.Refinery,new NavPoint(47.50000000000001,71.01408311032397)),
                (PlayableBuildingKind.Refinery,new NavPoint(-44.249999999999986,-65.38491798572512)),
                (PlayableBuildingKind.Refinery,new NavPoint(-85.25,-5.629165124598859)),
                (PlayableBuildingKind.Refinery,new NavPoint(-34.499999999999986,71.01408311032398)),
                (PlayableBuildingKind.Refinery,new NavPoint(37.75000000000001,76.64324823492282)),
                (PlayableBuildingKind.Refinery,new NavPoint(-44.249999999999986,76.64324823492284)),
                (PlayableBuildingKind.SentryTurret,new NavPoint(-77.75,-7.361215932167739)),
                (PlayableBuildingKind.Mine,new NavPoint(71.00000000000001,-122.97560733739029)),
                (PlayableBuildingKind.ScientificCenter,new NavPoint(-125.25861046343715,-3.8206041399010893)),
                (PlayableBuildingKind.Factory,new NavPoint(-44.249999999999986,65.38491798572514))
            };
            var obstacles=map.MovementBlockers.Concat(buildings.Select(b=>{
                double size=TerritoryRules.Radius(profile,b.Item1);
                return new NavObstacle(b.Item2.X-size,b.Item2.Z-size,b.Item2.X+size,b.Item2.Z+size);
            })).ToArray();
            var geometry=new NavGeometry(map.HalfExtent,obstacles,92);
            var session=new NavigationSession(1,geometry,profile.Navigation,null,map);
            var budget=session.PhysicalHostBudget;
            try
            {
                var start=new NavLocation(new NavPoint(-58.786464769451641,108.03145523548126),"city/street");
                var goal=new NavLocation(new NavPoint(2.8200000000000034,52.0),"city/plaza/0");
                var mover=session.Crowd.Add(91,start.Position,radius,PlayableUnitRules.Speed(profile,PlayableEntityKind.Explorer),
                    PlayableUnitRules.Turn(profile,PlayableEntityKind.Explorer),start);
                var peers=new (int Id,NavLocation Pose,double Radius,double Heading)[]{
                    (83,new NavLocation(new NavPoint(-59.19966212400606,106.7411177140195),"city/street"),0.58,1.2928982836967604),
                (98,new NavLocation(new NavPoint(-55.772136884927086,100.30194555318901),"city/street"),0.58,-1.6158718125738438),
                (102,new NavLocation(new NavPoint(-53.67400405769558,107.05649826900425),"city/street"),0.58,2.2283217588652797),
                (107,new NavLocation(new NavPoint(-57.94518018681752,100.72965098912273),"city/street"),0.58,-2.372402629379617),
                (114,new NavLocation(new NavPoint(-55.85140623938652,104.94998486374676),"city/street"),0.58,1.56967765578828),
                (119,new NavLocation(new NavPoint(-55.89138953656281,107.10491581005114),"city/street"),0.58,0.0),
                (121,new NavLocation(new NavPoint(-54.12831091024006,101.06504148495316),"city/street"),0.58,-0.7392658899869307),
                (125,new NavLocation(new NavPoint(-55.91063226080128,102.82273924688577),"city/street"),0.58,0.03672323767049658),
                (130,new NavLocation(new NavPoint(-57.99138953656282,107.10491581005114),"city/street"),0.58,-1.5707963267948966)
                };
                foreach(var p in peers)
                {
                    var peer=session.Crowd.Add(p.Id,p.Pose.Position,p.Radius,mover.Speed,mover.TurnSpeed,p.Pose);
                    peer.Heading=p.Heading;
                    peer.Team=5;
                }
                mover.Team=5;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));
                using(scope)
                {
                    var reservation=scope.OwnedReservation();
                    var provider=PartitionNavigationProvider.CopyOwned(new PartitionNavigationProvider(map,geometry,""),reservation);
                    NavTypedLeg[] typed=null,shared=null;
                    NavRecastSpine spine=null;
                    NavRecastMemberPlan plan=null;
                    NavTypedRoute route=null;
                    NavPreparedCrowdStep frame=null;
                    try
                    {
                        var recorded=new (NavLocation From,NavLocation To,double Cost,string Portal)[]{
                            (new NavLocation(new NavPoint(-60.09138953656283,107.10491581005114),"city/street"),new NavLocation(new NavPoint(-50.02438013895052,106.45222603991476),"city/street"),10.088145624823982,null),
                (new NavLocation(new NavPoint(-50.02438013895052,106.45222603991476),"city/street"),new NavLocation(new NavPoint(-50.02437973022461,106.45222473144531),"city/street"),1.3708206197076609e-06,null),
                (new NavLocation(new NavPoint(-50.02437973022461,106.45222473144531),"city/street"),new NavLocation(new NavPoint(-49.89316940307617,106.0886459350586),"city/street"),0.38653032369067125,null),
                (new NavLocation(new NavPoint(-49.89316940307617,106.0886459350586),"city/street"),new NavLocation(new NavPoint(-49.340972900390625,104.55853271484375),"city/street"),1.6267044735459444,null),
                (new NavLocation(new NavPoint(-49.340972900390625,104.55853271484375),"city/street"),new NavLocation(new NavPoint(-47.569183349609375,99.64897155761719),"city/street"),5.2194855080558895,null),
                (new NavLocation(new NavPoint(-47.569183349609375,99.64897155761719),"city/street"),new NavLocation(new NavPoint(-46.2435417175293,95.9756851196289),"city/street"),3.905170801925643,null),
                (new NavLocation(new NavPoint(-46.2435417175293,95.9756851196289),"city/street"),new NavLocation(new NavPoint(-45.126399993896484,92.880126953125),"city/street"),3.2909703725330495,null),
                (new NavLocation(new NavPoint(-45.126399993896484,92.880126953125),"city/street"),new NavLocation(new NavPoint(-44.79999923706055,91.9756851196289),"city/street"),0.9615365225725171,null),
                (new NavLocation(new NavPoint(-44.79999923706055,91.9756851196289),"city/street"),new NavLocation(new NavPoint(-44.20000076293945,90.77568054199219),"city/street"),1.3416441984731722,null),
                (new NavLocation(new NavPoint(-44.20000076293945,90.77568054199219),"city/street"),new NavLocation(new NavPoint(-42.0,86.9756851196289),"city/street"),4.39089610067422,null),
                (new NavLocation(new NavPoint(-42.0,86.9756851196289),"city/street"),new NavLocation(new NavPoint(-41.44390106201172,86.14154052734375),"city/street"),1.0025184435561592,null),
                (new NavLocation(new NavPoint(-41.44390106201172,86.14154052734375),"city/street"),new NavLocation(new NavPoint(-41.08799743652344,85.60768127441406),"city/street"),0.6416175594342372,null),
                (new NavLocation(new NavPoint(-41.08799743652344,85.60768127441406),"city/street"),new NavLocation(new NavPoint(-40.7675666809082,85.12703704833984),"city/street"),0.5776631727941767,null),
                (new NavLocation(new NavPoint(-40.7675666809082,85.12703704833984),"city/street"),new NavLocation(new NavPoint(-39.4666633605957,83.1756820678711),"city/street"),2.3452368129040364,null),
                (new NavLocation(new NavPoint(-39.4666633605957,83.1756820678711),"city/street"),new NavLocation(new NavPoint(-32.775508880615234,73.13894653320312),"city/street"),12.062653458830251,null),
                (new NavLocation(new NavPoint(-32.775508880615234,73.13894653320312),"city/street"),new NavLocation(new NavPoint(-32.75833511352539,73.1131820678711),"city/street"),0.030963623009939267,null),
                (new NavLocation(new NavPoint(-32.75833511352539,73.1131820678711),"city/street"),new NavLocation(new NavPoint(-32.74399948120117,73.09168243408203),"city/street"),0.025840754772238822,null),
                (new NavLocation(new NavPoint(-32.74399948120117,73.09168243408203),"city/street"),new NavLocation(new NavPoint(-32.400001525878906,72.57568359375),"city/street"),0.6201527202954934,null),
                (new NavLocation(new NavPoint(-32.400001525878906,72.57568359375),"city/street"),new NavLocation(new NavPoint(-32.400001525878906,70.37568664550781),"city/street"),2.1999969482421875,null),
                (new NavLocation(new NavPoint(-32.400001525878906,70.37568664550781),"city/street"),new NavLocation(new NavPoint(-32.400001525878906,69.37568664550781),"city/street"),1.0,null),
                (new NavLocation(new NavPoint(-32.400001525878906,69.37568664550781),"city/street"),new NavLocation(new NavPoint(-32.509891510009766,68.4599380493164),"city/street"),0.9223184374384,null),
                (new NavLocation(new NavPoint(-32.509891510009766,68.4599380493164),"city/street"),new NavLocation(new NavPoint(-33.56288528442383,59.68496322631836),"city/street"),8.83792843562361,null),
                (new NavLocation(new NavPoint(-33.56288528442383,59.68496322631836),"city/street"),new NavLocation(new NavPoint(-33.599998474121094,59.37568283081055),"city/street"),0.31149920047244767,null),
                (new NavLocation(new NavPoint(-33.599998474121094,59.37568283081055),"city/street"),new NavLocation(new NavPoint(-33.599998474121094,58.77568435668945),"city/street"),0.5999984741210938,null),
                (new NavLocation(new NavPoint(-33.599998474121094,58.77568435668945),"city/street"),new NavLocation(new NavPoint(-33.39999847412109,58.57568435668945),"city/street"),0.28284271247462306,null),
                (new NavLocation(new NavPoint(-33.39999847412109,58.57568435668945),"city/street"),new NavLocation(new NavPoint(-33.199998474121095,58.375684356689455),"city/street"),0.28284271247461296,null),
                (new NavLocation(new NavPoint(-33.199998474121095,58.375684356689455),"city/street"),new NavLocation(new NavPoint(-33.0,58.37568283081055),"city/street"),0.199998474126916,null),
                (new NavLocation(new NavPoint(-33.0,58.37568283081055),"city/street"),new NavLocation(new NavPoint(-33.0,58.37568283081055),"city/ramp/0/-1"),0.0,"8aaea741eaaadbe39602f58a29d2b25e18abc1c72c516c915b60d99dab9caa9e"),
                (new NavLocation(new NavPoint(-33.0,58.37568283081055),"city/ramp/0/-1"),new NavLocation(new NavPoint(-29.200000762939453,57.81686019897461),"city/ramp/0/-1"),3.8408692942500378,null),
                (new NavLocation(new NavPoint(-29.200000762939453,57.81686019897461),"city/ramp/0/-1"),new NavLocation(new NavPoint(-28.339860916137695,57.69036865234375),"city/ramp/0/-1"),0.869390975007918,null),
                (new NavLocation(new NavPoint(-28.339860916137695,57.69036865234375),"city/ramp/0/-1"),new NavLocation(new NavPoint(-27.56000518798828,57.57568359375),"city/ramp/0/-1"),0.7882433757362662,null),
                (new NavLocation(new NavPoint(-27.56000518798828,57.57568359375),"city/ramp/0/-1"),new NavLocation(new NavPoint(-16.999999999999996,56.02274112351564),"city/ramp/0/-1"),10.673581399239765,null),
                (new NavLocation(new NavPoint(-16.999999999999996,56.02274112351564),"city/ramp/0/-1"),new NavLocation(new NavPoint(-16.999999999999996,56.02274112351564),"city/plaza/0"),0.0,"a1fbebf73db2776f67e15241e620a5a06c42de3d4f2e8e3c84a7bdf41c3fa2c0"),
                (new NavLocation(new NavPoint(-16.999999999999996,56.02274112351564),"city/plaza/0"),new NavLocation(new NavPoint(-16.39999771118164,55.934505462646484),"city/plaza/0"),0.6064555040860637,null),
                (new NavLocation(new NavPoint(-16.39999771118164,55.934505462646484),"city/plaza/0"),new NavLocation(new NavPoint(-3.5999975204467773,54.052154541015625),"city/plaza/0"),12.937667868475256,null),
                (new NavLocation(new NavPoint(-3.5999975204467773,54.052154541015625),"city/plaza/0"),new NavLocation(new NavPoint(-2.1308770179748535,53.83610534667969),"city/plaza/0"),1.484921649500931,null),
                (new NavLocation(new NavPoint(-2.1308770179748535,53.83610534667969),"city/plaza/0"),new NavLocation(new NavPoint(-1.7873413562774658,53.785587310791016),"city/plaza/0"),0.34723021586248964,null),
                (new NavLocation(new NavPoint(-1.7873413562774658,53.785587310791016),"city/plaza/0"),new NavLocation(new NavPoint(-1.2003055810928345,53.699256896972656),"city/plaza/0"),0.5933497633745801,null),
                (new NavLocation(new NavPoint(-1.2003055810928345,53.699256896972656),"city/plaza/0"),new NavLocation(new NavPoint(1.0000027418136597,53.37568283081055),"city/plaza/0"),2.223973221993527,null),
                (new NavLocation(new NavPoint(1.0000027418136597,53.37568283081055),"city/plaza/0"),new NavLocation(new NavPoint(2.5625444120965453,52.97908452723586),"city/plaza/0"),1.612087679305548,null),
                (new NavLocation(new NavPoint(2.5625444120965453,52.97908452723586),"city/plaza/0"),new NavLocation(new NavPoint(2.8200000000000034,52.0),"city/plaza/0"),1.0123684562526567,null)
                        };
                        typed=recorded.Select(l=>NavTypedLeg.CreateOwned(l.From,l.To,l.Cost,
                            l.Portal==null?NavTypedLegKind.Surface:NavTypedLegKind.Portal,l.Portal,reservation,provider)).ToArray();
                        var recordedSpine=new (NavLocation From,NavLocation To,double Cost,string Portal)[]{
                            (new NavLocation(new NavPoint(-50.02438013895052,106.45222603991476),"city/street"),new NavLocation(new NavPoint(-50.02437973022461,106.45222473144531),"city/street"),1.3708206197076609e-06,null),
                (new NavLocation(new NavPoint(-50.02437973022461,106.45222473144531),"city/street"),new NavLocation(new NavPoint(-49.89316940307617,106.0886459350586),"city/street"),0.38653032369067125,null),
                (new NavLocation(new NavPoint(-49.89316940307617,106.0886459350586),"city/street"),new NavLocation(new NavPoint(-49.340972900390625,104.55853271484375),"city/street"),1.6267044735459444,null),
                (new NavLocation(new NavPoint(-49.340972900390625,104.55853271484375),"city/street"),new NavLocation(new NavPoint(-47.569183349609375,99.64897155761719),"city/street"),5.2194855080558895,null),
                (new NavLocation(new NavPoint(-47.569183349609375,99.64897155761719),"city/street"),new NavLocation(new NavPoint(-46.2435417175293,95.9756851196289),"city/street"),3.905170801925643,null),
                (new NavLocation(new NavPoint(-46.2435417175293,95.9756851196289),"city/street"),new NavLocation(new NavPoint(-45.126399993896484,92.880126953125),"city/street"),3.2909703725330495,null),
                (new NavLocation(new NavPoint(-45.126399993896484,92.880126953125),"city/street"),new NavLocation(new NavPoint(-44.79999923706055,91.9756851196289),"city/street"),0.9615365225725171,null),
                (new NavLocation(new NavPoint(-44.79999923706055,91.9756851196289),"city/street"),new NavLocation(new NavPoint(-44.20000076293945,90.77568054199219),"city/street"),1.3416441984731722,null),
                (new NavLocation(new NavPoint(-44.20000076293945,90.77568054199219),"city/street"),new NavLocation(new NavPoint(-42.0,86.9756851196289),"city/street"),4.39089610067422,null),
                (new NavLocation(new NavPoint(-42.0,86.9756851196289),"city/street"),new NavLocation(new NavPoint(-41.44390106201172,86.14154052734375),"city/street"),1.0025184435561592,null),
                (new NavLocation(new NavPoint(-41.44390106201172,86.14154052734375),"city/street"),new NavLocation(new NavPoint(-41.08799743652344,85.60768127441406),"city/street"),0.6416175594342372,null),
                (new NavLocation(new NavPoint(-41.08799743652344,85.60768127441406),"city/street"),new NavLocation(new NavPoint(-40.7675666809082,85.12703704833984),"city/street"),0.5776631727941767,null),
                (new NavLocation(new NavPoint(-40.7675666809082,85.12703704833984),"city/street"),new NavLocation(new NavPoint(-39.4666633605957,83.1756820678711),"city/street"),2.3452368129040364,null),
                (new NavLocation(new NavPoint(-39.4666633605957,83.1756820678711),"city/street"),new NavLocation(new NavPoint(-32.775508880615234,73.13894653320312),"city/street"),12.062653458830251,null),
                (new NavLocation(new NavPoint(-32.775508880615234,73.13894653320312),"city/street"),new NavLocation(new NavPoint(-32.75833511352539,73.1131820678711),"city/street"),0.030963623009939267,null),
                (new NavLocation(new NavPoint(-32.75833511352539,73.1131820678711),"city/street"),new NavLocation(new NavPoint(-32.74399948120117,73.09168243408203),"city/street"),0.025840754772238822,null),
                (new NavLocation(new NavPoint(-32.74399948120117,73.09168243408203),"city/street"),new NavLocation(new NavPoint(-32.400001525878906,72.57568359375),"city/street"),0.6201527202954934,null),
                (new NavLocation(new NavPoint(-32.400001525878906,72.57568359375),"city/street"),new NavLocation(new NavPoint(-32.400001525878906,70.37568664550781),"city/street"),2.1999969482421875,null),
                (new NavLocation(new NavPoint(-32.400001525878906,70.37568664550781),"city/street"),new NavLocation(new NavPoint(-32.400001525878906,69.37568664550781),"city/street"),1.0,null),
                (new NavLocation(new NavPoint(-32.400001525878906,69.37568664550781),"city/street"),new NavLocation(new NavPoint(-32.509891510009766,68.4599380493164),"city/street"),0.9223184374384,null),
                (new NavLocation(new NavPoint(-32.509891510009766,68.4599380493164),"city/street"),new NavLocation(new NavPoint(-33.56288528442383,59.68496322631836),"city/street"),8.83792843562361,null),
                (new NavLocation(new NavPoint(-33.56288528442383,59.68496322631836),"city/street"),new NavLocation(new NavPoint(-33.599998474121094,59.37568283081055),"city/street"),0.31149920047244767,null),
                (new NavLocation(new NavPoint(-33.599998474121094,59.37568283081055),"city/street"),new NavLocation(new NavPoint(-33.599998474121094,58.77568435668945),"city/street"),0.5999984741210938,null),
                (new NavLocation(new NavPoint(-33.599998474121094,58.77568435668945),"city/street"),new NavLocation(new NavPoint(-33.39999847412109,58.57568435668945),"city/street"),0.28284271247462306,null),
                (new NavLocation(new NavPoint(-33.39999847412109,58.57568435668945),"city/street"),new NavLocation(new NavPoint(-33.199998474121095,58.375684356689455),"city/street"),0.28284271247461296,null),
                (new NavLocation(new NavPoint(-33.199998474121095,58.375684356689455),"city/street"),new NavLocation(new NavPoint(-33.0,58.37568283081055),"city/street"),0.199998474126916,null),
                (new NavLocation(new NavPoint(-33.0,58.37568283081055),"city/street"),new NavLocation(new NavPoint(-33.0,58.37568283081055),"city/ramp/0/-1"),0.0,"8aaea741eaaadbe39602f58a29d2b25e18abc1c72c516c915b60d99dab9caa9e"),
                (new NavLocation(new NavPoint(-33.0,58.37568283081055),"city/ramp/0/-1"),new NavLocation(new NavPoint(-29.200000762939453,57.81686019897461),"city/ramp/0/-1"),3.8408692942500378,null),
                (new NavLocation(new NavPoint(-29.200000762939453,57.81686019897461),"city/ramp/0/-1"),new NavLocation(new NavPoint(-28.339860916137695,57.69036865234375),"city/ramp/0/-1"),0.869390975007918,null),
                (new NavLocation(new NavPoint(-28.339860916137695,57.69036865234375),"city/ramp/0/-1"),new NavLocation(new NavPoint(-27.56000518798828,57.57568359375),"city/ramp/0/-1"),0.7882433757362662,null),
                (new NavLocation(new NavPoint(-27.56000518798828,57.57568359375),"city/ramp/0/-1"),new NavLocation(new NavPoint(-16.999999999999996,56.02274112351564),"city/ramp/0/-1"),10.673581399239765,null),
                (new NavLocation(new NavPoint(-16.999999999999996,56.02274112351564),"city/ramp/0/-1"),new NavLocation(new NavPoint(-16.999999999999996,56.02274112351564),"city/plaza/0"),0.0,"a1fbebf73db2776f67e15241e620a5a06c42de3d4f2e8e3c84a7bdf41c3fa2c0"),
                (new NavLocation(new NavPoint(-16.999999999999996,56.02274112351564),"city/plaza/0"),new NavLocation(new NavPoint(-16.39999771118164,55.934505462646484),"city/plaza/0"),0.6064555040860637,null),
                (new NavLocation(new NavPoint(-16.39999771118164,55.934505462646484),"city/plaza/0"),new NavLocation(new NavPoint(-3.5999975204467773,54.052154541015625),"city/plaza/0"),12.937667868475256,null),
                (new NavLocation(new NavPoint(-3.5999975204467773,54.052154541015625),"city/plaza/0"),new NavLocation(new NavPoint(-2.1308770179748535,53.83610534667969),"city/plaza/0"),1.484921649500931,null),
                (new NavLocation(new NavPoint(-2.1308770179748535,53.83610534667969),"city/plaza/0"),new NavLocation(new NavPoint(-1.7873413562774658,53.785587310791016),"city/plaza/0"),0.34723021586248964,null),
                (new NavLocation(new NavPoint(-1.7873413562774658,53.785587310791016),"city/plaza/0"),new NavLocation(new NavPoint(-1.2003055810928345,53.699256896972656),"city/plaza/0"),0.5933497633745801,null),
                (new NavLocation(new NavPoint(-1.2003055810928345,53.699256896972656),"city/plaza/0"),new NavLocation(new NavPoint(1.0000027418136597,53.37568283081055),"city/plaza/0"),2.223973221993527,null),
                (new NavLocation(new NavPoint(1.0000027418136597,53.37568283081055),"city/plaza/0"),new NavLocation(new NavPoint(2.5625444120965453,52.97908452723586),"city/plaza/0"),1.612087679305548,null),
                (new NavLocation(new NavPoint(2.5625444120965453,52.97908452723586),"city/plaza/0"),new NavLocation(new NavPoint(6.420000076293945,52.0),"city/plaza/0"),3.9797701834052273,null),
                (new NavLocation(new NavPoint(6.420000076293945,52.0),"city/plaza/0"),new NavLocation(new NavPoint(6.4200000000000035,52.0),"city/plaza/0"),7.62939418308406e-08,null)
                        };
                        shared=recordedSpine.Select(l=>NavTypedLeg.CreateOwned(l.From,l.To,l.Cost,
                            l.Portal==null?NavTypedLegKind.Surface:NavTypedLegKind.Portal,l.Portal,reservation,provider)).ToArray();
                        var graph=new string('a',64);
                        spine=NavRecastSpine.Create(1,88,89,2305843009213694179,radius,graph,graph,graph,
                            provider.Binding,graph,"",geometry.Revision,new NavPoint(6.4200000000000035,51.1),4.290795063055894,39,shared,reservation);
                        plan=NavRecastMemberPlan.Create(spine,0,39,1,40,graph,graph,reservation);
                        var subscription=new NavRouteSubscription(1,88,89,2305843009213694179,13517,89,91,mover.Incarnation,89,
                            mover.MobilityRevision,268,recorded[0].From,goal,"");
                        route=NavTypedRoute.CopySubscriptionAndCreateOwned(subscription,typed,typed.Sum(l=>l.Cost),reservation,plan);
                        frame=NavPreparedCrowdStep.Create(session.Crowd,session.Crowd.Units.Count,reservation);
                        frame.Observe();
                        int row=frame.Find(91);
                        var actor=frame.After[row];
                        actor.Heading=-0.72165690661789217;
                        actor.route=typed.Where(l=>l.Kind==NavTypedLegKind.Surface).Select(l=>l.To.Position).ToArray();
                        actor.routeIndex=2;
                        actor.Moving=true;
                        actor.Outcome=NavigationOutcome.Moving;
                        actor.Goal=goal.Position;
                        actor.GoalLocation=goal;
                        actor.InstalledRequest=268;
                        actor.InstalledOrder=1;
                        actor.InstalledGroupId=88;
                        actor.InstalledRevision=89;
                        frame.BindRecast(row,route,new NavRecastStepState(0,0,0,0));
                        frame.Formation[row]=new NavFormationFrameHint{Flags=NavFormationFrameHint.Bound,TargetLeg=27,ForwardLimit=-.58,Target=new NavPoint(-25.18,53.8)};
                        int repairs=0;
                        double separation=0;
                        for(int tick=0;tick<300;tick++)
                        {
                            var before=actor.Location;
                            frame.Compute(1d/30);
                            if(!actor.Location.Equals(before)){
                                Assert.True(frame.RecastReceipts[row].Verified);
                                Assert.True(RecastFormationMotion.Allows(frame.Formation[row],route,before.Position,actor.Position));
                            }
                            if(frame.RecastReceipts[row].Verified)frame.RecastStates[row]=frame.RecastReceipts[row].After;
                            separation=Math.Max(separation,LayeredNavigationProvider.Distance(start.Position,actor.Position));
                            if(repairs!=frame.Working.RepairCount||tick%30==0||tick==299)
                            {
                                repairs=frame.Working.RepairCount;
                                TestContext.WriteLine($"ANTENNA_MOTOR tick={tick} x={actor.Position.X:R} z={actor.Position.Z:R} separation={separation:R} ix={actor.routeIndex} li={actor.localIndex} local={string.Join(" ",actor.localRoute.Select(p=>$"{p.X:R},{p.Z:R}"))} repairs={repairs} failures={frame.Working.RepairFailed} outcome={actor.Outcome}");
                            }
                            if(separation>5)break;
                        }
                        Assert.Greater(separation,5,"The accepted route leaves the occupied factory apron in a bounded window.");
                        Assert.AreEqual(268,actor.InstalledRequest);
                        Assert.AreEqual(goal,actor.GoalLocation);
                    }
                    finally
                    {
                        frame?.Release();route?.ReleaseOwner();plan?.Release();spine?.Release();
                        if(typed!=null)foreach(var leg in typed)leg.ReleaseOwner();
                        if(shared!=null)foreach(var leg in shared)leg.ReleaseOwner();
                        ((INavStorageFacade)provider).StorageRoot.Release();
                    }
                    scope.Discard();
                }
            }
            finally
            {
                session.DestroyNavigationAuthority();
                Assert.Zero(budget.PhysicalBytes);
                Assert.Zero(budget.HeldBytes);
            }
        }

        [Test] public void OccupiedAcceptedEndpointsAllowOnlyTypedPartialProgressWithoutCounterOrInstallCredit()
        {
            ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;var s=Ready();
            byte[] Bytes(){using var capture=s.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,capture));}
            try{
                var route=Route(s);var sourceActor=s.Crowd.Units.Single(u=>u.Id==2);Assert.AreEqual(12,sourceActor.route.Length);
                int[] occupied={6,8,9,10};for(int n=0;n<occupied.Length;n++)s.Crowd.Add(101+n,sourceActor.route[occupied[n]],.72,4,4.5,new NavLocation(sourceActor.route[occupied[n]],"deck/-1/1"),canYield:false);
                byte[] source=Bytes();int publications=s.OwnedMovementPublications;var map=PlayableProfile.RecastGroupCorridorDefault.AuthoredMap;
                Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();var frame=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,r);NavLocalRepairWorkspace scratch=null;
                    try{
                        frame.Observe();int row=frame.Find(2);var actor=frame.After[row];frame.After[frame.Find(1)].Moving=false;
                        var at=actor.Location;var state=NavRecastIntervals.Initial(route);
                        for(int n=0;n<=6;n++){Assert.True(NavRecastIntervals.Advance(route,state,at,actor.route[n],r,out var receipt,frame.Working,actor.Id));at=new NavLocation(receipt.To,receipt.ToSurface);state=receipt.After;}
                        var start=At(-48,42.4,"deck/-1/1");var witness=new NavPoint(-44.4,42.4);
                        Assert.True(NavRecastIntervals.Advance(route,state,at,start.Position,r,out var setup,frame.Working,actor.Id));state=setup.After;Assert.AreEqual(start.SurfaceId,setup.ToSurface);Assert.AreEqual(6,NavRecastIntervals.FirstTargetSurface(route,state));
                        actor.Position=start.Position;actor.Location=start;actor.routeIndex=actor.route.Length-1;actor.localRoute=Array.Empty<NavPoint>();actor.localIndex=0;actor.Heading=Math.PI;actor.noProgressTicks=10;actor.blockedSeconds=10d/30;frame.BindRecast(row,route,state);
                        var peers=occupied.Select((_,n)=>frame.After[frame.Find(101+n)]).ToArray();foreach(var peer in peers){peer.Moving=false;peer.Held=false;peer.Outcome=NavigationOutcome.Stopped;Assert.False(peer.CanYield);Assert.True(map.IsValidLocation(peer.Location,peer.Radius));}
                        Assert.True(map.IsValidLocation(actor.Location,actor.Radius));Assert.AreEqual(new NavPoint(-42.4,42.4),actor.Goal);
                        for(int n=6;n<actor.route.Length;n++)Assert.True(peers.Any(peer=>Math.Pow(actor.route[n].X-peer.Position.X,2)+Math.Pow(actor.route[n].Z-peer.Position.Z,2)<Math.Pow(actor.Radius+peer.Radius,2)),"Every accepted connector endpoint is actually occupied");
                        Assert.True(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,new[]{witness},0,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out _,frame.Working,actor.Id));
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="partial-only-endpoint-cover",Start=start,Witness=witness,Goal=actor.Goal,PhysicalFirst=6,Occupied=peers.Select(v=>new{v.Id,v.Position,v.Radius,v.Held}),FanCenter=route.RecastPlan.Spine.FanCenter,FanRadius=route.RecastPlan.Spine.FanRadius,Horizon=120,Dt=1d/30}));
                        var binding=route.Subscription;Assert.True(binding.EntityId==actor.Id&&binding.Incarnation==actor.Incarnation&&binding.MobilityRevision==actor.MobilityRevision&&binding.RequestSequence==actor.InstalledRequest&&binding.GroupId==actor.InstalledGroupId&&binding.ActivationRevision==actor.InstalledRevision&&binding.MemberOrderRevision==actor.InstalledRevision&&binding.AssignedEndpoint.Equals(actor.GoalLocation.Value));
                        var build=typeof(NavCrowd).GetMethod("BuildIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);build.Invoke(frame.Working,null);var repair=typeof(NavCrowd).GetMethod("CommitDetour",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                        bool Repair(){object[] args={actor,actor.route[actor.routeIndex],scratch};try{return (bool)repair.Invoke(frame.Working,args);}finally{scratch=(NavLocalRepairWorkspace)args[2];}}
                        var header=new NavUnitSaveState();actor.CopyCaptureHeaderTo(header);var originalLocal=actor.localRoute;int repairs=frame.Working.RepairCount;
                        peers[3].Held=true;try{Assert.False(Repair(),"HOLD-invalid final suffix must reject partial progress too");Assert.AreSame(originalLocal,actor.localRoute);Assert.True(actor.SameCaptureHeader(header));}finally{peers[3].Held=false;}
                        bool installed=Repair();TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="partial-only-install-before-assert",installed,Local=actor.localRoute,actor.routeIndex,actor.noProgressTicks,actor.blockedSeconds,RepairCpuMs=frame.Working.RepairCpuMs}));Assert.True(installed);Assert.AreEqual(repairs+1,frame.Working.RepairCount);Assert.AreEqual(state,frame.RecastStates[row]);Assert.AreEqual(10,actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds);
                        var prefix=actor.localRoute;Assert.Greater(prefix.Length,0);Assert.Greater(Math.Sqrt(Math.Pow(prefix[0].X-start.Position.X,2)+Math.Pow(prefix[0].Z-start.Position.Z,2)),frame.Profile.ArrivalTolerance);
                        double InitialDistance=Math.Sqrt(Math.Pow(actor.Goal.X-start.Position.X,2)+Math.Pow(actor.Goal.Z-start.Position.Z,2));var end=prefix[prefix.Length-1];Assert.Less(Math.Sqrt(Math.Pow(actor.Goal.X-end.X,2)+Math.Pow(actor.Goal.Z-end.Z,2)),InitialDistance);Assert.True(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,prefix,0,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out int selected,frame.Working,actor.Id));Assert.AreEqual(actor.routeIndex,selected);
                        actor.routeIndex=5;Assert.False(NavRecastIntervals.CanTarget(route,state,actor.Location,5,r),"Consumed ramp target is not the physical deck continuation");
                        bool suffixUpdated=Repair();TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="same-prefix-new-suffix-before-assert",suffixUpdated,actor.routeIndex,actor.localIndex,SamePrefix=ReferenceEquals(prefix,actor.localRoute),Repairs=frame.Working.RepairCount}));Assert.True(suffixUpdated);Assert.AreEqual(selected,actor.routeIndex);Assert.AreSame(prefix,actor.localRoute);Assert.Zero(actor.localIndex);Assert.AreEqual(repairs+1,frame.Working.RepairCount);Assert.AreEqual(10,actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds);
                        Assert.False(Repair(),"Identical unfinished prefix must not be reinstalled");Assert.AreSame(prefix,actor.localRoute);Assert.AreEqual(repairs+1,frame.Working.RepairCount);Assert.AreEqual(10,actor.noProgressTicks);Assert.AreEqual(10d/30,actor.blockedSeconds);CollectionAssert.AreEqual(source,Bytes());Assert.AreEqual(publications,s.OwnedMovementPublications);
                        bool moved=false;int steps=0;
                        for(;steps<120&&!moved;steps++){
                            var before=actor.Location;double heading=actor.Heading;frame.Compute(1d/30);var receipt=frame.RecastReceipts[row];if(receipt.Verified)frame.RecastStates[row]=receipt.After;
                            Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(actor.Heading-heading)),actor.TurnSpeed/30+1e-12);Assert.AreSame(prefix,actor.localRoute);Assert.AreEqual(repairs+1,frame.Working.RepairCount);
                            foreach(var peer in peers)Assert.GreaterOrEqual(Math.Pow(actor.Position.X-peer.Position.X,2)+Math.Pow(actor.Position.Z-peer.Position.Z,2),Math.Pow(actor.Radius+peer.Radius,2)-1e-9);
                            if(actor.Position.Equals(before.Position))Assert.AreEqual(10,actor.noProgressTicks);else{Assert.True(receipt.Verified);Assert.Zero(actor.noProgressTicks);}
                            double distance=Math.Sqrt(Math.Pow(actor.Goal.X-actor.Position.X,2)+Math.Pow(actor.Goal.Z-actor.Position.Z,2));moved=InitialDistance-distance>=frame.Profile.LocalGridCell;
                        }
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="partial-only-real-progress-before-assert",moved,steps,actor.Position,actor.Outcome,actor.noProgressTicks,Repairs=frame.Working.RepairCount}));Assert.True(moved);Assert.AreNotEqual(NavigationOutcome.Arrived,actor.Outcome);Assert.AreEqual(new NavPoint(-42.4,42.4),actor.Goal);CollectionAssert.AreEqual(source,Bytes());Assert.AreEqual(publications,s.OwnedMovementPublications);
                    }finally{scratch?.ReleaseOwner();frame.Release();}scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(Budget(s).PhysicalBytes);Assert.Zero(Budget(s).HeldBytes);Assert.Zero(Budget(s).ActiveScopes);Assert.AreEqual(native,RecastNative.sw_recast_allocation_count());Assert.AreEqual(faults,RecastNative.ReleaseFaults);}
        }
        private sealed class HoldAuthority:INavRecastSweepAuthority
        {public bool AllowsRecastSweep(int actor,NavLocation from,NavPoint to,double radius)=>from.Position.Equals(to);}
        [Test] public void ContinuationRejoinPreservesValidCursorAndRequiresActualFloorAndGoalBinding()
        {
            var s=Ready();try{var route=Route(s);s.Crowd.TryGet(2,out var actor);var state=NavRecastIntervals.Initial(route);Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();
                Assert.True(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out int same,s.Crowd,actor.Id));Assert.AreEqual(actor.routeIndex,same);
                Assert.False(NavRecastIntervals.TryContinuationRejoin(route,state,new NavLocation(actor.Position,"deck/-1/1"),Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,actor.GoalLocation.Value,r,out _,s.Crowd,actor.Id));
                Assert.False(NavRecastIntervals.TryContinuationRejoin(route,state,actor.Location,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,new NavLocation(actor.Goal,"bank/west"),r,out _,s.Crowd,actor.Id));scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        }
        [Test] public void CurrentHoldAuthorityDeniesAnOtherwiseClearEntryConnection()
        {
            var s=Ready();try{var route=Route(s);var pose=At(-27.6,43.5,"bank/west");var state=NavRecastIntervals.Initial(route);Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){Assert.True(NavRecastIntervals.Advance(route,state,pose,pose.Position,scope.OwnedReservation(),out var receipt,new HoldAuthority(),2));Assert.AreEqual(0,receipt.After.Phase);scope.Discard();}}finally{s.ReleaseCommittedHostRoot();}
        }
        [Test] public void ProductionClearanceCrossesAcceptedSeamAndNextBindingRemainsValid()
        {
            var s=Ready();try{var route=Route(s);Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var frame=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,scope.OwnedReservation());try{frame.Observe();int row=frame.Find(2);var actor=frame.After[row];actor.Position=new NavPoint(-29.49,45.58);actor.Location=new NavLocation(actor.Position,actor.Location.NameReference);actor.Heading=Math.PI;actor.Moving=true;actor.Outcome=NavigationOutcome.Moving;
                frame.BindRecast(row,route,new NavRecastStepState(1,route.RecastPlan.OriginLegs+1,1.7,2.2));frame.Preferred[row]=4;frame.RecastReceiver[row]=4;
                frame.Working.ProductionEscape=u=>u.Id==2?(NavPoint?)new NavPoint(-1,0):null;frame.Compute(1d/30);
                Assert.Less(frame.Motion[row].YieldLabel,0);Assert.Less(actor.Position.X,-29.5);Assert.AreEqual("ramp/-1/1/x",actor.Location.SurfaceId);Assert.True(actor.Moving);Assert.True(frame.RecastReceipts[row].Verified);
                Assert.True(NavRecastIntervals.Advance(route,frame.RecastReceipts[row].After,actor.Location,actor.Position,scope.OwnedReservation(),out _,frame.Working,2),"Next PrepareOwned zero sweep must accept the reconciled actual support.");
            }finally{frame.Release();}scope.Discard();}}finally{s.ReleaseCommittedHostRoot();}
        }
        [Test] public void IncompatibleProducerSweepKeepsActiveOwnRouteExecutionWithoutManualReissue()
        {
            var s=Ready();try{var route=Route(s);Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var frame=NavPreparedCrowdStep.Create(s.Crowd,s.Crowd.Units.Count,scope.OwnedReservation());try{frame.Observe();int row=frame.Find(2);var actor=frame.After[row];actor.Position=new NavPoint(-29.49,-45);actor.Location=new NavLocation(actor.Position,actor.Location.NameReference);actor.Heading=Math.PI;actor.Moving=true;actor.Outcome=NavigationOutcome.Moving;
                frame.BindRecast(row,route,new NavRecastStepState(1,route.RecastPlan.OriginLegs+1,1.7,2.2));frame.Preferred[row]=4;frame.RecastReceiver[row]=4;var oldGoal=actor.Goal;var oldRequest=actor.InstalledRequest;
                frame.Working.ProductionEscape=u=>u.Id==2?(NavPoint?)new NavPoint(-1,0):null;frame.Compute(1d/30);Assert.Less(frame.Motion[row].YieldLabel,0);Assert.AreEqual("ramp/-1/-1/x",actor.Location.SurfaceId);Assert.True(frame.RecastReceipts[row].ProductionUnmapped);Assert.AreEqual(3,frame.RecastReceipts[row].After.Phase);Assert.True(actor.Moving);
                // The next frame uses the existing same-intent own-route lane,
                // as PrepareOwned does for explicit unmapped producer state.
                var retained=frame.RecastRoutes[row];frame.RecastRoutes[row]=null;retained.Release();frame.Working.ProductionEscape=null;var displaced=actor.Position;
                for(int i=0;i<30;i++)frame.Compute(1d/30);
                Assert.True(actor.Moving);Assert.AreEqual(oldRequest,actor.InstalledRequest);Assert.AreEqual(oldGoal,actor.Goal);Assert.AreNotEqual(displaced,actor.Position,"Existing kinematics/route recovery continue without a new command or global query.");
            }finally{frame.Release();}scope.Discard();}}finally{s.ReleaseCommittedHostRoot();}
        }
        [Test] public void ActualLegalBackwardSweepLowersPhysicalStationButNotHighWater()
        {
            var s=Ready();try{var route=Route(s);var p=route.RecastPlan;int i=Enumerable.Range(p.OriginLegs,p.TerminalLeg-p.OriginLegs).First(n=>route.Legs[n].Kind==NavTypedLegKind.Surface&&NavRecastSpine.Length(route.Legs[n])>.5);var l=route.Legs[i];
                NavLocation Point(double t)=>At(l.From.Position.X+t*(l.To.Position.X-l.From.Position.X),l.From.Position.Z+t*(l.To.Position.Z-l.From.Position.Z),l.From.SurfaceId);
                var state=new NavRecastStepState(1,i,0,0);Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();Assert.True(NavRecastIntervals.Advance(route,state,Point(.5),Point(.7).Position,r,out var a));Assert.True(NavRecastIntervals.Advance(route,a.After,Point(.7),Point(.3).Position,r,out var b));Assert.Less(b.After.Station,a.After.Station);Assert.AreEqual(a.After.HighWater,b.After.HighWater);scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();}
        }
    }
}

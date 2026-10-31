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
        private static NavigationSession Ready()
        {
            var profile=PlayableProfile.RecastGroupCorridorDefault;var map=profile.AuthoredMap;
            var g=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var s=new NavigationSession(801,g,profile.Navigation,null,map){UsesRecastGroundRoutes=true};
            try{s.ConfigureMarch(4,profile.Revision);s.Crowd.Add(1,new NavPoint(-27.25,42),.72,4,4.5,canYield:true);s.Crowd.Add(2,new NavPoint(-27.25,48),.72,4,4.5,canYield:true);
                Assert.True(s.MoveGroup(new[]{1,2},new[]{new NavPoint(-46,46),new NavPoint(-42.4,42.4)}));
                using(var host=new UnityHostRouteService()){var binding=new PlayableRouteBinding(g,profile,s.Admission,s.RoutePort);for(int i=0;i<200&&s.AppliedResults<2;i++){host.Service(binding,8);s.ApplyResults();}}
                Assert.AreEqual(2,s.AppliedResults);return s;
            }catch{s.ReleaseCommittedHostRoot();throw;}
        }
        private static NavigationRetentionBudget Budget(NavigationSession s)=>s.PhysicalHostBudget;
        private static NavTypedRoute Route(NavigationSession s)=>((NavigationInstalledExecutionOwner)s.Crowd.Units.Single(u=>u.Id==2).StorageOwner.InstalledExecution).Corridor.TypedRoute;
        private static NavLocation At(double x,double z,string surface)=>new NavLocation(new NavPoint(x,z),surface);
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
        [Test] public void PhysicalRejoinRemainsAvailableAfterForecastTargetAheadAndDisplacementOutsideFan()
        {
            var s=Ready();try{var route=Route(s);var p=route.RecastPlan;var fan=p.Spine.Node(p.Exit);double dx=fan.Position.X-p.Spine.FanCenter.X,dz=fan.Position.Z-p.Spine.FanCenter.Z,len=Math.Sqrt(dx*dx+dz*dz);
                var outside=At(fan.Position.X+dx/len,fan.Position.Z+dz/len,fan.SurfaceId);Assert.False(NavRecastIntervals.InTerminalFan(route,outside));
                var state=new NavRecastStepState(1,p.TerminalLeg-1,p.Spine.Station(p.Exit),p.Spine.Station(p.Exit));int forecast=route.Legs.Count(l=>l.Kind==NavTypedLegKind.Surface);
                Assert.Less(NavRecastIntervals.FirstTargetSurface(route,state),forecast);
                Assert.True(Budget(s).TryOpenScope(NavigationPreparationKind.Admission,801,1,out var scope));using(scope){var r=scope.OwnedReservation();Assert.False(NavRecastIntervals.CanTarget(route,state,outside,forecast,r));Assert.True(Enumerable.Range(NavRecastIntervals.FirstTargetSurface(route,state),forecast-NavRecastIntervals.FirstTargetSurface(route,state)).Any(i=>NavRecastIntervals.CanTarget(route,state,outside,i,r)),"Physical interval supplies safe forward fan/shared rejoin despite an earlier forecast endpoint.");scope.Discard();}
            }finally{s.ReleaseCommittedHostRoot();}
        }
        private sealed class HoldAuthority:INavRecastSweepAuthority
        {public bool AllowsRecastSweep(int actor,NavLocation from,NavPoint to,double radius)=>from.Position.Equals(to);}
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

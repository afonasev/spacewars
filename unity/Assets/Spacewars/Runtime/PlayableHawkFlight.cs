using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Aircraft never enter the ground crowd or its route/footprint authority.
        private sealed class AirState
        {
            public NavUnit Actor;
            public double Height, PreviousHeight, Pitch, Roll, YawVelocity, ShortRemaining;
            public bool Short, Escape, Departing;
            public int FacingTarget;
            public NavPoint Velocity,YieldGoal; public bool Yielding;
            public readonly Queue<PlayableCommand> Deferred=new Queue<PlayableCommand>();
        }
        private bool TryActor(int id,out NavUnit actor)
        {
            if(units.TryGetValue(id,out var u)&&u.Air!=null){actor=u.Air.Actor;return true;}
            return navigation.Crowd.TryGet(id,out actor);
        }
        private void InitializeHawk(Unit unit,NavPoint p)
        {
            unit.Air=new AirState{Height=ClearHeight(p),PreviousHeight=ClearHeight(p),Actor=new NavUnit(unit.Id,p,PlayableUnitRules.Radius(profile,unit.Kind),PlayableUnitRules.Speed(profile,unit.Kind),PlayableUnitRules.Turn(profile,unit.Kind)){Team=TeamOf(unit.Owner)}};
        }
        private static double ApproachAir(double value,double target,double delta)=>value+Math.Max(-delta,Math.Min(delta,target-value));
        private bool AirInside(NavPoint p)=>AirInside(profile,p);
        private static bool AirInside(PlayableProfile profile,NavPoint p)=>!double.IsNaN(p.X)&&!double.IsNaN(p.Z)&&!double.IsInfinity(p.X)&&!double.IsInfinity(p.Z)&&Math.Abs(p.X)<=profile.ArenaHalfExtent-profile.HawkRadius&&Math.Abs(p.Z)<=profile.ArenaHalfExtent-profile.HawkRadius;
        private double AirSurface(NavPoint p)=>profile.AuthoredMap?.SupportAt(p)?.HeightAt(p)??0;
#if UNITY_INCLUDE_TESTS
        [ThreadStatic] internal static bool DisableAirClearanceBoundsForTests;
        [ThreadStatic] internal static long AirClearanceExactQueriesForTests;
        [ThreadStatic] internal static long AirSweepExactQueriesForTests;
#endif
        private bool AirSolidWithinRadius(NavObstacle solid,NavPoint point)
        {
            double radius=profile.HawkRadius;
#if UNITY_INCLUDE_TESTS
            if(!DisableAirClearanceBoundsForTests)
#endif
            {
                // Conservative AABB only: keep the original polygon distance and
                // radius comparison for every possible hit, including edge contact.
                double bound=radius+1e-7;
                if(point.X+bound<solid.MinX||point.X-bound>solid.MaxX||point.Z+bound<solid.MinZ||point.Z-bound>solid.MaxZ)return false;
            }
#if UNITY_INCLUDE_TESTS
            AirClearanceExactQueriesForTests++;
#endif
            return solid.DistanceSquared(point)<=radius*radius;
        }
        private double ClearHeight(NavPoint p)
        {
            double surface=AirSurface(p),h=surface;
            var solids=SolidObstacles();
            for(int i=0;i<solids.Count;i++){
                var solid=solids[i];
                if(AirSolidWithinRadius(solid,p))h=Math.Max(h,double.IsNaN(solid.Top)?surface+profile.BallisticWallHeight:solid.Top);
            }
            foreach(var b in buildings.Values)if(b.Phase!=ConstructionPhase.Pending&&Distance(b.Position,p)<BuildingRadius(b.Kind)+profile.HawkRadius)
                h=Math.Max(h,AirSurface(b.Position)+BuildingHeightForAir(b.Kind));
            return h+profile.HawkClearance;
        }
        private double BuildingHeightForAir(PlayableBuildingKind k)=>PlayableUnitRules.AirBuildingHeight(profile,k);
        private PlayableCommandStatus ApplyAirCommand(PlayableCommand command,out string message,bool recordHuman)
        {
            message="Invalid aircraft order.";var ids=command.CopyEntityIds();var owner=OwnerFor(command.PlayerId);
            var invalid=MoveRecipientFailure(ids,owner);if(invalid.HasValue)return invalid.Value;
            bool spatial=command.Kind==PlayableCommandKind.Move||command.Kind==PlayableCommandKind.AttackMove;
            if(spatial&&!AirInside(command.Target))return PlayableCommandStatus.InvalidTarget;
            if(command.Kind==PlayableCommandKind.Attack&&!VisibleTarget(owner,command.TargetId))return PlayableCommandStatus.InvalidTarget;
            if(command.Kind==PlayableCommandKind.Follow&&(!units.TryGetValue(command.TargetId,out var leader)||Hostile(leader.Owner,owner)||ids.Contains(command.TargetId)))return PlayableCommandStatus.InvalidTarget;
            int[] ground=ids.Where(id=>units[id].Air==null).OrderBy(id=>id).ToArray();
            var aircraft=ids.Where(id=>units[id].Air!=null).OrderBy(id=>id).ToArray();
            NavPoint[] destinations = Array.Empty<NavPoint>();
            if (spatial)
            {
                if (ReplayAirAssignments != null)
                {
                    if (ReplayAirAssignments.Count == 0) throw new InvalidOperationException("Missing recorded air assignment.");
                    destinations = ReplayAirAssignments.Peek().Resolve(Tick, command, aircraft);
                    ReplayAirAssignments.Dequeue();
                }
                else
                {
                    var direction = ground.Length == 0
                        ? AirFormationDirection(command.Target, aircraft)
                        : navigation.GroundFormationDirection(command.Target, ground);
                    var groundSlots = ground.Any(id => units[id].Kind == PlayableEntityKind.Tank) ||
                        aircraft.Any(id => units[id].Kind == PlayableEntityKind.Skat)
                        ? PreviewMixedGroundSlots(command, ground) : null;
                    destinations = AirFormationDestinations(command.Target, aircraft, AirArrivalFootprints(aircraft),
                        i => units[aircraft[i]].Air.Actor.Position, i => units[aircraft[i]].Kind,
                        direction: direction, groundIds: ground, groundSlots: groundSlots, objective: RelayArrivalArea(command));
                    AirAssignmentRecorded?.Invoke(new RecordedAirAssignment(Tick, command.Generation, command.Sequence,
                        command.PlayerId, command.Kind, command.Mode, command.Target, aircraft, destinations));
                }
            }
            if(destinations==null)return PlayableCommandStatus.InvalidTarget;
            if(command.Mode==PlayableOrderMode.Append&&aircraft.Any(id=>(units[id].Air.Actor.Moving||units[id].CurrentOrder!=null)&&units[id].Air.Deferred.Count>=profile.UnitOrderQueueLimit))return PlayableCommandStatus.Overflow;
            if(ground.Length>0)
            {
                // Validate/admit all ground effects first; no air mutation occurs on a refused ground transaction.
                var groundCommand=RecipientCommand(command,ground);
                if(spatial){if(!navigation.Crowd.Locate(command.Target,0,out var location)||command.TargetLocation.HasValue&&!command.TargetLocation.Value.Equals(location))return PlayableCommandStatus.InvalidTarget;groundCommand=groundCommand.WithResolvedLocation(location);}
                PlayableCommandStatus status;
                if(command.Mode==PlayableOrderMode.Append)status=AppendTactical(groundCommand,out message);
                else status=ApplyTacticalPrepared(groundCommand,out message,out _);
                if(status!=PlayableCommandStatus.Applied&&status!=PlayableCommandStatus.Accepted)return status;
            }
            for(int i=0;i<aircraft.Length;i++)
            {
                var u=units[aircraft[i]];var air=u.Air;
                if(command.Mode==PlayableOrderMode.Append&&(air.Actor.Moving||u.CurrentOrder!=null)){air.Deferred.Enqueue(new PlayableCommand(command.Generation,command.Sequence,command.PlayerId,command.Kind,new[]{u.Id},spatial?destinations[i]:command.Target,targetId:command.TargetId,origin:command.Origin,source:command.Source,jobId:command.JobId,actionId:command.ActionId));continue;}
                air.Deferred.Clear();SetAirOrder(u,command,spatial?destinations[i]:command.Target);
            }
            if(recordHuman&&command.Origin==PlayableOrderOrigin.Human)RecordHumanAction(command.PlayerId);
            message="Aircraft order accepted.";return PlayableCommandStatus.Applied;
        }
        private NavPoint[] PreviewMixedGroundSlots(PlayableCommand command, int[] ids)
        {
            if (ids.Length == 0 || command.Mode != PlayableOrderMode.Replace || !profile.UsesRecastGroundRoutes)
                return null;
            var relay = RelayArrivalArea(command);
            if (ids.Length > 1 || relay.HasValue)
                return navigation.AllocateFormationSlots(command.Target, ids, id => units[id].Kind, relay);
            if (command.Kind == PlayableCommandKind.Move && command.Origin == PlayableOrderOrigin.Human)
                return new[] { navigation.ResolveSingleMoveEndpoint(profile, ids[0], command.Target, command.TargetLocation).Position };
            return navigation.AllocateArrivalSlots(command.Target, ids);
        }

        private NavPoint AirFormationDirection(NavPoint target, int[] aircraft)
        {
            var members = aircraft.Select(id => units[id]).ToArray();
            var center = new NavPoint(members.Average(unit => unit.Air.Actor.Position.X),
                members.Average(unit => unit.Air.Actor.Position.Z));
            if (NavFormationLayout.TryDirection(center, target, profile.Navigation.ArrivalTolerance, out var direction))
                return direction;
            var stamp = members[0].LastOrder;
            if (stamp != null && members.All(unit => AirFormationOrder(unit) && SameFormationCommand(unit.LastOrder, stamp)))
            {
                var prior = new NavPoint(members.Average(unit => unit.Air.Actor.Goal.X),
                    members.Average(unit => unit.Air.Actor.Goal.Z));
                if (NavFormationLayout.TryDirection(center, prior, profile.Navigation.ArrivalTolerance, out direction))
                    return direction;
            }
            return NavFormationLayout.DirectionFromHeadings(members.Select(unit => unit.Air.Actor.Heading));
        }

        private NavPoint[] AirArrivalFootprints(int[] replacing)
        {
            var occupied=new List<NavPoint>();
            foreach(var unit in units.Values)
            {
                var air=unit.Air;
                if(air==null||Array.IndexOf(replacing,unit.Id)>=0)continue;
                occupied.Add(air.Actor.Position);
                if(air.Actor.Moving)occupied.Add(air.Actor.Goal);
                if(unit.HasAttackMove)occupied.Add(unit.AttackMove);
                if(air.Yielding)occupied.Add(air.YieldGoal);
            }
            return occupied.ToArray();
        }
        private static NavPoint[] ObservedAirArrivalFootprints(PlayableAiObservation observation,int[] replacing)
        {
            var occupied=new List<NavPoint>();
            foreach(var entity in observation.Entities)
            {
                if(!PlayableUnitRules.Aircraft(entity.Kind)||Array.IndexOf(replacing,entity.Id)>=0)continue;
                occupied.Add(entity.Position);
                var order=entity.CurrentOrder;
                if(order?.Kind==PlayableTacticalOrderKind.Move||order?.Kind==PlayableTacticalOrderKind.AttackMove)
                    occupied.Add(order.Destination);
            }
            return occupied.ToArray();
        }
        private NavPoint[] AirFormationDestinations(NavPoint target, int[] aircraft, NavPoint[] occupied,
            Func<int, NavPoint> position, Func<int, PlayableEntityKind> kind, NavPoint? origin = null,
            NavPoint? direction = null, int[] groundIds = null, NavPoint[] groundSlots = null,
            (NavPoint Center, double Radius)? objective = null)
        {
            bool alignGround = groundIds != null && groundSlots != null && groundSlots.Length > 0 &&
                groundSlots.Length == groundIds.Length;
            if (aircraft.Length == 0 || aircraft.Length < 2 && !alignGround && !objective.HasValue) return AirDestinations(target, aircraft, occupied, origin: position);
            var members = aircraft.Select((id, i) => new NavFormationLayout.Member(id, kind(i), position(i), profile.HawkRadius)).ToArray();
            var center = origin ?? new NavPoint(members.Average(m => m.Position.X), members.Average(m => m.Position.Z));
            var approach = direction ?? NavFormationLayout.Direction(center, target);
            double pitch = 2 * profile.HawkRadius + profile.HawkSeparation + NavFormationLayout.Gap(profile.Navigation);
            var offsets = alignGround ? MixedAirOffsets(members, target, approach, pitch, groundIds, groundSlots) : null;
            var slots = NavFormationLayout.Destinations(members, target, approach, pitch,
                profile.ArenaHalfExtent, (_, point) => AirInside(point) && occupied.All(p => NavFormationLayout.Separated(p, point, pitch)),
                (_, point, _, other) => NavFormationLayout.Separated(point, other, pitch), offsets, objective);
            return slots.Length == aircraft.Length ? slots : null;
        }

        private NavPoint[] MixedAirOffsets(NavFormationLayout.Member[] members, NavPoint target, NavPoint approach,
            double pitch, int[] groundIds, NavPoint[] groundSlots)
        {
            var offsets = NavFormationLayout.Offsets(members, pitch, approach);
            var side = NavFormationLayout.Side(approach);
            var ground = groundSlots.Select(point => new NavPoint(point.X - target.X, point.Z - target.Z))
                .Select(delta => new NavPoint(NavFormationLayout.Dot(delta, approach), NavFormationLayout.Dot(delta, side))).ToArray();
            var tanks = Enumerable.Range(0, groundIds.Length).Where(i => units[groundIds[i]].Kind == PlayableEntityKind.Tank).ToArray();
            var hawks = Enumerable.Range(0, members.Length).Where(i => members[i].Kind == PlayableEntityKind.Hawk).ToArray();
            if (tanks.Length != 0 && hawks.Length != 0)
            {
                var shift = new NavPoint(tanks.Average(i => ground[i].X) - hawks.Average(i => offsets[i].X),
                    tanks.Average(i => ground[i].Z) - hawks.Average(i => offsets[i].Z));
                foreach (int i in hawks)
                    offsets[i] = new NavPoint(offsets[i].X + shift.X, offsets[i].Z + shift.Z);
            }
            var skats = Enumerable.Range(0, members.Length).Where(i => members[i].Kind == PlayableEntityKind.Skat).ToArray();
            if (skats.Length != 0)
            {
                double front = ground.Concat(hawks.Select(i => offsets[i])).Max(point => point.X);
                double shift = front - pitch - skats.Max(i => offsets[i].X);
                foreach (int i in skats) offsets[i] = new NavPoint(offsets[i].X + shift, offsets[i].Z);
            }
            return offsets;
        }

        // Each endpoint needs clearance for both aircraft's legal arrival error.
        // Observed AI may supply only footprints from its owner projection.
        private NavPoint[] AirDestinations(NavPoint target,int[] aircraft,NavPoint[] occupied,double arrivalClearance=0,
            Predicate<NavPoint> approachClear=null,Func<int,NavPoint> origin=null)
        {
            if(aircraft.Length==0)return Array.Empty<NavPoint>();
            var destinations=new List<NavPoint>();
            double spacing=2*profile.HawkRadius+profile.HawkSeparation+2*profile.Navigation.ArrivalTolerance+arrivalClearance;
            double minimumSquared=spacing*spacing-1e-9;
            for(int member=0;member<aircraft.Length;member++)
            {
                bool found=false;
                var from=origin?.Invoke(member)??default;
                int limit=(int)Math.Ceiling(2*profile.ArenaHalfExtent/spacing);
                for(int ring=0;ring<=limit&&!found;ring++)
                {
                    NavPoint closest=default;
                    double distance=double.PositiveInfinity;
                    for(int slot=0;slot<(ring==0?1:8*ring);slot++)
                    {
                        double angle=slot*Math.PI/(4*Math.Max(1,ring));
                        var p=new NavPoint(target.X+ring*spacing*Math.Cos(angle),target.Z+ring*spacing*Math.Sin(angle));
                        if(!AirInside(p)||destinations.Any(other=>AirArrivalOverlap(other,p,minimumSquared))||
                            occupied.Any(other=>AirArrivalOverlap(other,p,minimumSquared))||approachClear?.Invoke(p)==false)continue;
                        // Among equally close arrival slots, take the one on
                        // the approach side. This avoids forcing an aircraft
                        // to fly past an already reserved destination.
                        double approach=origin==null?0:Distance(from,p);
                        if(approach>=distance)continue;
                        closest=p;distance=approach;
                        if(origin==null)break;
                    }
                    if(distance<double.PositiveInfinity){destinations.Add(closest);found=true;}
                }
                if(!found)return null;
            }
            return destinations.ToArray();
        }
        private static bool AirArrivalOverlap(NavPoint a,NavPoint b,double minimumSquared)
        {
            double x=a.X-b.X,z=a.Z-b.Z;
            return x*x+z*z<minimumSquared;
        }
        private void SetAirOrder(Unit u,PlayableCommand command,NavPoint goal)
        {
            SetPreparedAirOrder(u, command, goal, ref nextOrderRevision, VisibleTarget);
            NotifyFlightDecision(u);
        }
        private void SetPreparedAirOrder(Unit u,PlayableCommand command,NavPoint goal,ref long revision,Func<PlayableOwner,int,bool> visible)
        {
            var a=u.Air;var n=a.Actor;bool previousShort=a.Short&&n.Moving;bool stationary=!n.Moving&&Distance(default,a.Velocity)<1e-5;
            bool sameAttack=command.Kind==PlayableCommandKind.Attack&&u.ExplicitTarget&&u.Target==command.TargetId&&
                u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==command.TargetId;
            if(!sameAttack)
            {
                u.DroneRepair=default;
                ResetDefaultCombat(u);
                InterruptBurst(u);u.StoppedSeconds=0;
                a.Short=false;a.Escape=false;a.FacingTarget=0;n.Held=false;
            }
            u.LastOrder=new PlayableOrderStamp(u.Id,u.Owner,navigation.Generation,revision++,command.Sequence,Tick,command.Origin,command.Kind,command.Source,command.JobId,command.ActionId);
            if(command.Kind==PlayableCommandKind.Stop||command.Kind==PlayableCommandKind.Hold)
            {n.Goal=n.Position;n.Moving=false;n.Held=command.Kind==PlayableCommandKind.Hold;n.Outcome=n.Held?NavigationOutcome.Held:NavigationOutcome.Stopped;u.CurrentOrder=null;u.Target=0;u.ExplicitTarget=false;u.HasAttackMove=false;return;}
            if(command.Kind==PlayableCommandKind.Attack&&u.Kind==PlayableEntityKind.Skat)
            {
                goal = command.Target;
                if (Target(command.TargetId,out var point,out _)) goal = point;
                n.Goal = goal;
                n.Moving = true;
                n.Outcome = NavigationOutcome.Moving;
                u.Target = 0;
                u.ExplicitTarget = false;
                u.HasAttackMove = true;
                u.AttackMove = goal;
                u.CurrentOrder = new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,
                    command.Sequence,Tick,PlayableTacticalOrderKind.AttackMove,goal,0);
                return;
            }
            if(command.Kind==PlayableCommandKind.Attack||command.Kind==PlayableCommandKind.Follow)
            {u.HasAttackMove=false;u.Target=command.Kind==PlayableCommandKind.Attack?command.TargetId:0;u.ExplicitTarget=u.Target!=0;u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,command.Sequence,Tick,command.Kind==PlayableCommandKind.Attack?PlayableTacticalOrderKind.Attack:PlayableTacticalOrderKind.Follow,goal,command.TargetId);if(!sameAttack)n.Moving=false;return;}
            double distance=Distance(n.Position,goal);double angle=Math.Atan2(goal.Z-n.Position.Z,goal.X-n.Position.X);bool sideways=Math.Abs(Angle(angle-n.Heading))>Math.PI/4;
            if(stationary&&!previousShort&&sideways)
            {
                a.Short=distance<=profile.HawkShortDistance;
                if(!a.Short&&u.Target!=0&&visible(u.Owner,u.Target)){a.Short=true;a.Escape=true;a.FacingTarget=u.Target;}
            }
            a.ShortRemaining=profile.HawkShortSeconds;n.Goal=goal;n.Moving=true;n.Outcome=NavigationOutcome.Moving;
            u.HasAttackMove=command.Kind==PlayableCommandKind.AttackMove;u.AttackMove=goal;u.ExplicitTarget=false;
            u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,command.Sequence,Tick,u.HasAttackMove?PlayableTacticalOrderKind.AttackMove:PlayableTacticalOrderKind.Move,goal,0);
        }
        private static bool AirFormationOrder(Unit unit)
            => unit.LastOrder != null && (unit.LastOrder.Kind == PlayableCommandKind.Move ||
                unit.LastOrder.Kind == PlayableCommandKind.AttackMove) &&
                (unit.CurrentOrder?.Kind == PlayableTacticalOrderKind.Move ||
                unit.CurrentOrder?.Kind == PlayableTacticalOrderKind.AttackMove) && !unit.DroneRepair.Detour;

        private static bool SameFormationCommand(PlayableOrderStamp stamp, PlayableOrderStamp order)
            => stamp != null && stamp.Owner == order.Owner && stamp.Generation == order.Generation &&
                stamp.Sequence == order.Sequence && stamp.Origin == order.Origin && stamp.Kind == order.Kind &&
                stamp.Source == order.Source && stamp.JobId == order.JobId && stamp.ActionId == order.ActionId;

        private void CalculateHawks(AirCalculation frame,double dt)
        {
            var all=frame.Units;
            for (int i = 0; i < all.Length; i++)
                frame.Traces[i].CruiseSpeed = all[i].Air.Actor.Speed;
            foreach(var u in all)
            {
                var a=u.Air;var n=a.Actor;var trace=frame.Trace(u.Id);a.PreviousHeight=a.Height;
                bool skat=u.Kind==PlayableEntityKind.Skat;
                double yawSpeed=PlayableUnitRules.Turn(profile,u.Kind);
                double yawAcceleration=skat?profile.SkatYawAcceleration:profile.HawkYawAcceleration;
                double forwardAcceleration=skat?profile.SkatAcceleration:profile.HawkAcceleration;
                double braking=skat?profile.SkatBraking:profile.HawkBraking;
                if(Frozen(u))continue;
                if (n.Outcome == NavigationOutcome.Waiting && AirFormationOrder(u))
                    n.Outcome = NavigationOutcome.Moving;
                if(a.Departing)
                {
                    double safe=ClearHeight(n.Position);a.Height=ApproachAir(a.Height,safe,profile.HawkClimbSpeed*dt);
                    if(a.Height>=safe-1e-7)a.Departing=false;
                    else{double aim=n.Heading;if(frame.Target(u.Target,out var liftTarget,out _)&&frame.VisibleTarget(u.Owner,u.Target)&&Distance(n.Position,liftTarget)>1e-9)aim=Math.Atan2(liftTarget.Z-n.Position.Z,liftTarget.X-n.Position.X);AdvanceAirYaw(a,aim,dt,yawSpeed,yawAcceleration);continue;}
                }
                if(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&frame.TryActor(u.CurrentOrder.TargetId,out var leader))
                {n.Goal=PlayableUnitRules.FollowGoal(profile,leader.Position,leader.Heading);n.Moving=Distance(n.Position,n.Goal)>profile.FollowArrivalTolerance;}
                bool yielding=a.Yielding;var savedGoal=n.Goal;bool savedMoving=n.Moving;
                if(yielding){n.Goal=a.YieldGoal;n.Moving=true;}
                var flightGoal=n.Goal;
                NavPoint waypoint=default;
                bool improveHawkApproach=u.Kind==PlayableEntityKind.Hawk;
                bool formationTraffic = AirFormationOrder(u) && all.Any(peer => peer.Id != u.Id && SameFormationCommand(peer.LastOrder, u.LastOrder));
                bool detouring=(improveHawkApproach||formationTraffic)&&n.Moving&&!a.Short&&TryAirTrafficWaypoint(u,all,out waypoint);
                if(detouring)flightGoal=waypoint;
                double distance=Distance(n.Position,flightGoal);
                double desired=Math.Atan2(flightGoal.Z-n.Position.Z,flightGoal.X-n.Position.X);
                if(a.Short&&a.Escape&&(a.ShortRemaining<=0||!frame.VisibleTarget(u.Owner,a.FacingTarget))){a.Short=false;a.Escape=false;}
                double facing=desired;if(a.Short&&a.Escape&&frame.Target(a.FacingTarget,out var enemy,out _))facing=Math.Atan2(enemy.Z-n.Position.Z,enemy.X-n.Position.X);
                bool aimExists=frame.Target(u.Target,out var aimPoint,out _);bool idleAim=!n.Moving&&aimExists&&frame.VisibleTarget(u.Owner,u.Target);if(idleAim&&Distance(n.Position,aimPoint)>1e-9)facing=Math.Atan2(aimPoint.Z-n.Position.Z,aimPoint.X-n.Position.X);else if(idleAim)facing=n.Heading;
                AdvanceAirYaw(a,(n.Moving&&(!a.Short||a.Escape)||idleAim)?facing:n.Heading,dt,yawSpeed,yawAcceleration);
                double forward=0,right=0;
                double arrival=detouring?0:profile.Navigation.ArrivalTolerance;
                if(n.Moving&&distance>arrival)
                {
                    double relative=Angle(desired-n.Heading);
                    if(a.Short)
                    {
                        a.ShortRemaining=Math.Max(0,a.ShortRemaining-dt);
                        forward=Math.Cos(relative);right=Math.Sin(relative);
                        // Finite short orders are bounded by HawkShortDistance and finish at their goal.
                        // Only a long target-facing escape uses the timed turn budget.
                    }
                    else forward=Math.Max(0,Math.Cos(relative));
                }
                else if(!detouring)n.Moving=false;
                double cruise = !yielding && n.Moving ? trace.CruiseSpeed : n.Speed;
                double desiredSpeed=Math.Min(cruise,Math.Sqrt(2*braking*Math.Max(0,distance-arrival)));
                // Keep the commanded turning radius within the remaining distance.
                // At cruise speed a close, off-axis goal can otherwise keep rotating away from the nose.
                if(!a.Short)desiredSpeed=Math.Min(desiredSpeed,distance*yawSpeed);
                if(improveHawkApproach&&!detouring&&!a.Short&&n.Moving&&distance<=profile.HawkShortDistance)
                {
                    // Brake before turning into a narrow final approach. The
                    // motor may carry sideways momentum even with its nose on
                    // the goal. A strafe at full speed would add more of it.
                    var aim=new NavPoint(Math.Cos(desired),Math.Sin(desired));
                    double along=a.Velocity.X*aim.X+a.Velocity.Z*aim.Z;
                    double lateral=Math.Abs(a.Velocity.X*aim.Z-a.Velocity.Z*aim.X);
                    if(along<0||lateral>desiredSpeed)forward=0;
                }
                double vf=forward*(forward<0?Math.Min(n.Speed,profile.HawkReverseSpeed):desiredSpeed),vr=right*Math.Min(profile.HawkStrafeSpeed,desiredSpeed);
                var wanted=new NavPoint(vf*Math.Cos(n.Heading)-vr*Math.Sin(n.Heading),vf*Math.Sin(n.Heading)+vr*Math.Cos(n.Heading));
                double speed=Distance(default,a.Velocity);double acceleration=Distance(default,wanted)<speed?braking:forwardAcceleration;
                var delta=new NavPoint(wanted.X-a.Velocity.X,wanted.Z-a.Velocity.Z);double change=Distance(default,delta);double fraction=change>acceleration*dt?acceleration*dt/change:1;
                var velocity=new NavPoint(a.Velocity.X+delta.X*fraction,a.Velocity.Z+delta.Z*fraction);
                var next=new NavPoint(n.Position.X+velocity.X*dt,n.Position.Z+velocity.Z*dt);
                if (u.DroneRepair.Detour && !u.DroneRepair.Returning && !yielding)
                {
                    next = EdgeToward(u.DroneRepair.Anchor,next,profile.CombatResponsePursuitRadius);
                    velocity = new NavPoint((next.X-n.Position.X)/dt,(next.Z-n.Position.Z)/dt);
                }
                double required=ClearHeight(n.Position),nextClear=ClearHeight(next);trace.Nominal=next;trace.NominalClearance=nextClear;
                if(n.Moving&&distance>1e-9)
                {
                    double look=Math.Min(distance,profile.HawkLookahead);
                    // Sampling at at most a footprint radius plus exact solid sweep below prevents thin-wall misses.
                    int steps=Math.Max(1,(int)Math.Ceiling(look/profile.HawkRadius));
                    for(int k=1;k<=steps;k++){double t=look*k/steps/distance;required=Math.Max(required,ClearHeight(new NavPoint(n.Position.X+(flightGoal.X-n.Position.X)*t,n.Position.Z+(flightGoal.Z-n.Position.Z)*t)));}
                }
                double beforeHeight=a.Height,nominalHeight=Math.Max(required,nextClear);
                a.Height=ApproachAir(beforeHeight,nominalHeight,dt*(beforeHeight<nominalHeight?profile.HawkClimbSpeed:profile.HawkDescentSpeed));
                trace.AssignedHeight=a.Height;trace.HeightAssigned=true;
                bool geometryClear=AirInside(next)&&a.Height+1e-7>=nextClear&&AirSweepClear(n.Position,next,a.Height);
                if(!geometryClear||!AirSeparation(u,next,all))
                {
                    next=n.Position;
                    if(geometryClear&&n.Moving&&!detouring&&desiredSpeed>0)
                    {
                        // Preserve deterministic priority when both directions
                        // are clear. A parked peer can seal one side forever.
                        double sign=(u.Id&1)==0?1:-1;
                        // A final correction must fit within the braking
                        // distance available at the current approach speed.
                        double lateralSpeed=formationTraffic||improveHawkApproach&&distance<=profile.HawkShortDistance?
                            Math.Min(profile.HawkStrafeSpeed,desiredSpeed):profile.HawkStrafeSpeed;
                        double step=lateralSpeed*dt;
                        double x=-sign*Math.Sin(n.Heading)*step,z=sign*Math.Cos(n.Heading)*step;
                        double bypassHeight=double.PositiveInfinity;
                        NavPoint bypass=default;
                        for(int attempt=0;attempt<2;attempt++)
                        {
                            var side=new NavPoint(n.Position.X+x,n.Position.Z+z);
                            bool withinRepairLeash = !u.DroneRepair.Detour || u.DroneRepair.Returning || yielding ||
                                Distance(u.DroneRepair.Anchor,side) <= profile.CombatResponsePursuitRadius;
                            x=-x;z=-z;
                            if(!withinRepairLeash||!AirInside(side)||!AirSeparation(u,side,all))continue;

                            double sideHeight=ClearHeight(side);
                            if(a.Height>=sideHeight&&AirSweepClear(n.Position,side,a.Height))
                            {
                                next=side;
                                break;
                            }
                            if(improveHawkApproach&&sideHeight>a.Height&&sideHeight<bypassHeight&&AirSweepClear(n.Position,side,sideHeight))
                            {
                                bypassHeight=sideHeight;
                                bypass=side;
                            }
                        }
                        if(next.Equals(n.Position)&&!double.IsPositiveInfinity(bypassHeight))
                        {
                            // The direct step is blocked by a peer. Gain the clearance of a
                            // safe lateral step, using one vertical integration from the old height.
                            double desiredHeight=Math.Max(nominalHeight,bypassHeight);
                            a.Height=ApproachAir(beforeHeight,desiredHeight,dt*(beforeHeight<desiredHeight?profile.HawkClimbSpeed:profile.HawkDescentSpeed));
                            if(a.Height>=bypassHeight)next=bypass;
                        }
                    }
                    velocity=new NavPoint((next.X-n.Position.X)/dt,(next.Z-n.Position.Z)/dt);
                }
                double ax=(velocity.X-a.Velocity.X)/dt,az=(velocity.Z-a.Velocity.Z)/dt;
                a.Pitch=ApproachAir(a.Pitch,(velocity.X*Math.Cos(n.Heading)+velocity.Z*Math.Sin(n.Heading))/n.Speed*profile.HawkCruisePitch+(ax*Math.Cos(n.Heading)+az*Math.Sin(n.Heading))/forwardAcceleration*profile.HawkAccelerationPitch,profile.HawkAttitudeSpeed*dt);
                a.Roll=ApproachAir(a.Roll,Math.Max(-profile.HawkBank,Math.Min(profile.HawkBank,(-ax*Math.Sin(n.Heading)+az*Math.Cos(n.Heading))/profile.HawkAcceleration*profile.HawkBank+a.YawVelocity/yawSpeed*profile.HawkBank)),profile.HawkAttitudeSpeed*dt);
                trace.Final=next;n.Position=next;a.Velocity=velocity;
                if(yielding){if(Distance(n.Position,a.YieldGoal)<=profile.Navigation.ArrivalTolerance&&Distance(default,velocity)<.01){a.Yielding=false;a.Velocity=default;}n.Goal=savedGoal;n.Moving=savedMoving;if(!a.Yielding&&n.Held)n.Goal=n.Position;continue;}
                if(!n.Moving&&Distance(default,velocity)<1e-5){
                    // Braking inertia can carry an aircraft out of tolerance after the first crossing.
                    // A move is complete only after settling inside the configured arrival radius.
                    if(!n.Held&&u.Target==0&&(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove)&&Distance(n.Position,n.Goal)>profile.Navigation.ArrivalTolerance){n.Moving=true;n.Outcome=NavigationOutcome.Moving;continue;}
                    if(u.Kind==PlayableEntityKind.Hawk&&u.HasAttackMove&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove&&
                        (u.Target!=0||Distance(n.Position,u.AttackMove)>profile.Navigation.ArrivalTolerance))continue;
                    if(u.Kind==PlayableEntityKind.Hawk)u.HasAttackMove=false;
                    a.Short=false;n.Outcome=n.Held?NavigationOutcome.Held:NavigationOutcome.Arrived;if(u.DroneRepair.Detour||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Repair)continue;if(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove)u.CurrentOrder=null;if(a.Deferred.Count>0){var command=a.Deferred.Dequeue();SetPreparedAirOrder(u,command,command.Target,ref frame.NextRevision,frame.VisibleTarget);}}
            }
        }
        private static void AdvanceAirYaw(AirState a,double desired,double dt,double yawSpeed,double acceleration)
        {
            double error=Angle(desired-a.Actor.Heading);
            // Leave room for this step plus braking: |error| = speed*dt + speed²/(2*acceleration).
            // Rationalization avoids cancellation near alignment; no extra tuning or firing tolerance.
            double step=acceleration*dt;
            double brakingRate=2*acceleration*Math.Abs(error)/(Math.Sqrt(step*step+2*acceleration*Math.Abs(error))+step);
            double rate=Math.Sign(error)*Math.Min(yawSpeed,brakingRate);
            a.YawVelocity=ApproachAir(a.YawVelocity,rate,step);a.Actor.Heading=Angle(a.Actor.Heading+a.YawVelocity*dt);
        }
        private bool TryAirTrafficWaypoint(Unit self,Unit[] all,out NavPoint waypoint)
        {
            waypoint=default;
            var actor=self.Air.Actor;
            var from=actor.Position;
            var goal=actor.Goal;
            double spacing=2*profile.HawkRadius+profile.HawkSeparation;
            double lookahead=profile.HawkLookahead;
            double distance=Distance(from,goal);
            if(distance<=profile.Navigation.ArrivalTolerance)return false;
            var ahead=distance<=lookahead?goal:new NavPoint(
                from.X+(goal.X-from.X)*lookahead/distance,from.Z+(goal.Z-from.Z)*lookahead/distance);
            var blocker=default(NavPoint);
            bool blocked=false;
            foreach(var other in all)
            {
                if(other==self)continue;
                var position=other.Air.Actor.Position;
                if(SegmentDistance(position,from,ahead)>=spacing)continue;
                blocker=position;
                blocked=true;
                break;
            }
            if(!blocked)return false;

            // The certified traffic route also covers a single blocker once
            // the assigned slot is inside the lookahead. Repeated lateral
            // steps here can push straight into peers off the direct chord.
            if(distance>lookahead)
            {
                bool grouped=false;
                foreach(var other in all)
                {
                    if(other==self)continue;
                    var position=other.Air.Actor.Position;
                    if(!position.Equals(blocker)&&Distance(position,blocker)<2*spacing){grouped=true;break;}
                }
                if(!grouped)return false;
            }

            // Bounded scratch and work for the local search. Every candidate
            // chord is checked against all peers, including those outside the
            // search. The real motor still checks every committed flight step.
            const int peerLimit=8;
            const int samples=8;
            Span<NavPoint> points=stackalloc NavPoint[2+peerLimit*samples];
            points[0]=from;
            points[1]=ahead;
            int count=2,peers=0;
            double radius=(spacing+profile.Navigation.ArrivalTolerance)/Math.Cos(Math.PI/samples);
            foreach(var other in all)
            {
                if(other==self)continue;
                var position=other.Air.Actor.Position;
                if(SegmentDistance(position,from,ahead)>lookahead+spacing)continue;
                if(++peers>peerLimit)return false;
                for(int i=0;i<samples;i++)
                {
                    double angle=i*2*Math.PI/samples;
                    var point=new NavPoint(position.X+radius*Math.Cos(angle),position.Z+radius*Math.Sin(angle));
                    if(AirInside(point)&&AirTrafficChordClear(self,point,point,all,spacing))points[count++]=point;
                }
            }

            Span<double> cost=stackalloc double[count];
            Span<int> first=stackalloc int[count];
            Span<bool> visited=stackalloc bool[count];
            cost.Fill(double.PositiveInfinity);
            visited.Clear();
            cost[0]=0;
            for(int i=0;i<count;i++)
            {
                int at=-1;
                double best=double.PositiveInfinity;
                for(int j=0;j<count;j++)
                    if(!visited[j]&&cost[j]<best){at=j;best=cost[j];}
                if(at<0)return false;
                if(at==1){waypoint=points[first[at]];return first[at]!=1;}
                visited[at]=true;
                for(int j=1;j<count;j++)
                {
                    if(visited[j])continue;
                    double nextCost=best+Distance(points[at],points[j]);
                    if(nextCost>=cost[j]||!AirTrafficChordClear(self,points[at],points[j],all,spacing))continue;
                    cost[j]=nextCost;
                    first[j]=at==0?j:first[at];
                }
            }
            return false;
        }
        private static bool AirTrafficChordClear(Unit self,NavPoint from,NavPoint to,Unit[] all,double spacing)
        {
            foreach(var other in all)
                if(other!=self&&SegmentDistance(other.Air.Actor.Position,from,to)<spacing)return false;
            return true;
        }
        private bool AirSeparation(Unit self,NavPoint next,Unit[] all)
        {
            double spacing=2*profile.HawkRadius+profile.HawkSeparation;
            return AirTrafficChordClear(self,self.Air.Actor.Position,next,all,spacing);
        }
        private static double SegmentDistance(NavPoint p,NavPoint a,NavPoint b)
        {double dx=b.X-a.X,dz=b.Z-a.Z,len=dx*dx+dz*dz,t=len==0?0:Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Z-a.Z)*dz)/len));return Distance(p,new NavPoint(a.X+t*dx,a.Z+t*dz));}
        private bool AirSweepClear(NavPoint from,NavPoint to,double height)
        {
            double radius=profile.HawkRadius,extent=profile.ArenaHalfExtent;
            // Match SegmentFree's arena test before discarding disjoint solids.
            // An out-of-bounds endpoint still fails when a height-qualified solid is present.
            bool inside=Math.Abs(from.X)+radius<=extent&&Math.Abs(from.Z)+radius<=extent&&
                Math.Abs(to.X)+radius<=extent&&Math.Abs(to.Z)+radius<=extent;
            double minX=Math.Min(from.X,to.X)-radius,minZ=Math.Min(from.Z,to.Z)-radius;
            double maxX=Math.Max(from.X,to.X)+radius,maxZ=Math.Max(from.Z,to.Z)+radius;
            var solids=SolidObstacles();
            for(int i=0;i<solids.Count;i++)
            {
                var s=solids[i];
                if(inside&&(maxX<s.MinX||minX>s.MaxX||maxZ<s.MinZ||minZ>s.MaxZ))continue;
                double top=double.IsNaN(s.Top)?Math.Max(AirSurface(from),AirSurface(to))+profile.BallisticWallHeight:s.Top;
                if(height-radius>top)continue;
                // Exact planar segment predicate includes radius against thin authored solids.
#if UNITY_INCLUDE_TESTS
                AirSweepExactQueriesForTests++;
#endif
                var geometry=new NavGeometry(extent,new[]{s},1);
                if(!geometry.SegmentFree(from,to,radius))return false;
            }
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using static Spacewars.Runtime.WorldWire;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private sealed class QueueIssuance
        {
            internal long Id,Revision,Tick,GroupId; internal PlayableCommand Command;
        }
        private sealed class QueueExecution
        {
            internal QueueIssuance Issuance; internal PlayableOrderStamp Stamp;
            internal NavLocation? Assigned; internal long Incarnation,ActivationTick=-1,RouteRequest,RouteOrder;
            internal bool Arrived,Suspended,Failed,Interrupted;internal NavLocation? OverrideLocation,AttemptLocation; internal long AttemptRequest,AttemptOrder;internal TerminalRouteFailure TerminalFailure;
        }
        private sealed class TerminalRouteFailure
        {
            internal long Request,Order,GroupId,Revision,Incarnation,Tick;internal NavLocation? Location;internal NavigationOutcome Outcome;
        }
        private sealed class TacticalQueue
        {
            internal QueueExecution Active,Pending; internal readonly List<QueueIssuance> Deferred=new List<QueueIssuance>();
            internal PlayableTacticalCompletion Completion; internal bool OldMotionSuspended;
        }
        private sealed class TacticalBehavior
        {
            internal PlayableTacticalOrderSnapshot Order; internal int Target; internal bool Explicit,AttackMove;
            internal NavPoint Anchor; internal double Repath,Stopped,Reload;internal int BurstRemaining,BurstTarget,BurstSequence,BurstSize;internal double BurstSpread,BurstDelay;
            internal static TacticalBehavior Capture(Unit u)=>new TacticalBehavior{Order=u.CurrentOrder,Target=u.Target,Explicit=u.ExplicitTarget,AttackMove=u.HasAttackMove,Anchor=u.AttackMove,Repath=u.Repath,Stopped=u.StoppedSeconds,Reload=u.Reload,BurstRemaining=u.BurstRemaining,BurstTarget=u.BurstTarget,BurstSequence=u.BurstSequence,BurstSize=u.BurstSize,BurstSpread=u.BurstSpread,BurstDelay=u.BurstDelay};
            internal void Install(Unit u){u.CurrentOrder=Order;u.Target=Target;u.ExplicitTarget=Explicit;u.HasAttackMove=AttackMove;u.AttackMove=Anchor;u.Repath=Repath;u.StoppedSeconds=Stopped;u.Reload=Reload;u.BurstRemaining=BurstRemaining;u.BurstTarget=BurstTarget;u.BurstSequence=BurstSequence;u.BurstSize=BurstSize;u.BurstSpread=BurstSpread;u.BurstDelay=BurstDelay;}
        }
        // Persistent domain metadata carries values, never borrowed physical name edges.
        // Call only after the owning navigation/source path has accepted the location.
        private static NavLocation? DomainLocation(NavLocation? source)
        {
            if(!source.HasValue)return null;
            var value=source.Value;
            if(string.IsNullOrWhiteSpace(value.SurfaceId)||double.IsNaN(value.Position.X)||double.IsInfinity(value.Position.X)||double.IsNaN(value.Position.Z)||double.IsInfinity(value.Position.Z))throw new InvalidOperationException("Invalid domain location value.");
            return value.Detached();
        }
        private static PlayableCommand DomainCommand(PlayableCommand command)=>command.TargetLocation.HasValue?command.WithResolvedLocation(DomainLocation(command.TargetLocation).Value):command;
        private static void RequireDomainLocation(NavLocation? value)
        {
            if(!value.HasValue)return;
            var name=value.Value.NameReference;
            if(name.Owner!=null||name.Role!=NavNameRole.None||name.Index!=0)throw new InvalidOperationException("Persistent domain location contains a physical name selector.");
            // Validate presence and exact value, without rewriting the source.
            if(string.IsNullOrWhiteSpace(value.Value.SurfaceId)||double.IsNaN(value.Value.Position.X)||double.IsInfinity(value.Value.Position.X)||double.IsNaN(value.Value.Position.Z)||double.IsInfinity(value.Value.Position.Z))throw new InvalidOperationException("Invalid domain location value.");
        }
        private void ValidateDomainLocationValues()
        {
            void Issuance(QueueIssuance i){if(i!=null)RequireDomainLocation(i.Command.TargetLocation);}
            void Execution(QueueExecution e){if(e==null)return;Issuance(e.Issuance);RequireDomainLocation(e.Assigned);RequireDomainLocation(e.AttemptLocation);RequireDomainLocation(e.OverrideLocation);if(e.TerminalFailure!=null)RequireDomainLocation(e.TerminalFailure.Location);}
            void Receipt(PlayableSpatialCompletion c){if(c==null)return;RequireDomainLocation(c.OriginalLocation);RequireDomainLocation(c.AssignedLocation);}
            foreach(var u in units.Values)if(u.CurrentOrder!=null)RequireDomainLocation(u.CurrentOrder.DestinationLocation);
            foreach(var q in tacticalQueues.Values){Execution(q.Active);Execution(q.Pending);foreach(var i in q.Deferred)Issuance(i);if(q.Completion!=null){RequireDomainLocation(q.Completion.OriginalLocation);RequireDomainLocation(q.Completion.AssignedLocation);Receipt(q.Completion.SpatialReceipt);}}
            foreach(var e in spatialExecutions.Values){RequireDomainLocation(e.OriginalLocation);RequireDomainLocation(e.AssignedLocation);}
            foreach(var c in spatialCompletions.Values)Receipt(c);
        }
        private long queueSequence;
        // Sole-authority synchronous scope; false at every capture/tick boundary.
        private bool combatRequestScope;
        private AuthoritySlotMap<int,TacticalQueue> tacticalQueues=new AuthoritySlotMap<int,TacticalQueue>();
        private QueueIssuance NewIssuance(PlayableCommand command,long revision)=>new QueueIssuance{Id=++queueSequence,Revision=revision,Tick=Tick,Command=DomainCommand(command)};
        private TacticalQueue QueueFor(int id){if(!tacticalQueues.TryGetValue(id,out var q)){q=new TacticalQueue();tacticalQueues.Add(id,q);}return q;}
        private static bool QueueKind(PlayableCommandKind k)=>k==PlayableCommandKind.Move||k==PlayableCommandKind.AttackMove||k==PlayableCommandKind.Attack;
        private PlayableOrderStamp Stamp(Unit u,QueueIssuance i,long revision)=>new PlayableOrderStamp(u.Id,u.Owner,navigation.Generation,revision,i.Command.Sequence,i.Tick,i.Command.Origin,i.Command.Kind,i.Command.Source,i.Command.JobId,i.Command.ActionId);
        private PlayableTacticalOrderSnapshot Order(Unit u,QueueIssuance i)=>new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,i.Command.Sequence,i.Tick,
            i.Command.Kind==PlayableCommandKind.Move?PlayableTacticalOrderKind.Move:i.Command.Kind==PlayableCommandKind.AttackMove?PlayableTacticalOrderKind.AttackMove:PlayableTacticalOrderKind.Attack,i.Command.Target,i.Command.TargetId,i.Command.TargetLocation);
        private void InstallExecution(Unit u,QueueExecution e)
        {
            InterruptBurst(u);u.CurrentOrder=Order(u,e.Issuance);u.Target=e.Issuance.Command.Kind==PlayableCommandKind.Attack?e.Issuance.Command.TargetId:0;
            u.ExplicitTarget=e.Issuance.Command.Kind==PlayableCommandKind.Attack;u.HasAttackMove=e.Issuance.Command.Kind==PlayableCommandKind.AttackMove;
            u.AttackMove=e.Assigned?.Position??e.Issuance.Command.Target;u.Repath=0;u.StoppedSeconds=0;
        }
        private void RegisterAcceptedQueue(PlayableCommand command,long revision,Dictionary<int,TacticalBehavior> previous,IEnumerable<int> recipients,NavigationGroupCommand prepared=null)
        {
            QueueIssuance issuance=QueueKind(command.Kind)?NewIssuance(command,revision):null;
            foreach(int id in recipients){
                if(!QueueKind(command.Kind)){tacticalQueues.Remove(id);continue;}
                var u=units[id];var q=QueueFor(id);q.Deferred.Clear();q.Completion=null;
                var group=prepared==null?navigation.GroupFor(id):prepared.Groups[prepared.MemberGroups[id]];issuance.GroupId=group.GroupId;
                var e=new QueueExecution{Issuance=issuance,Stamp=u.LastOrder,Incarnation=group.Members.Single(m=>m.Entity==id).Incarnation,Assigned=DomainLocation(group.Members.Single(m=>m.Entity==id).GoalLocation)};
                if(command.Kind!=PlayableCommandKind.Attack){var request=prepared==null?navigation.PendingRequestFor(id):prepared.Pending.TryGetValue(id,out var pendingRequest)?pendingRequest:prepared.Allocating[id];e.AttemptRequest=request.Request;e.AttemptOrder=request.Order;e.AttemptLocation=DomainLocation(request.GoalLocation);}
                if(command.Kind==PlayableCommandKind.Attack){e.ActivationTick=Tick;q.Active=e;q.Pending=null;}
                else {q.Pending=e;if(previous.TryGetValue(id,out var old))old.Install(u);}
            }
        }
        private PlayableCommandStatus AppendTactical(PlayableCommand c,out string message)
        {
            message=null;
            if(c.Kind==PlayableCommandKind.Follow)return PlayableCommandStatus.Applied; // Prescribed silent no-op.
            if(!QueueKind(c.Kind))return PlayableCommandStatus.Rejected;
            var failure=MoveRecipientFailure(c.CopyEntityIds(),OwnerFor(c.PlayerId));if(failure.HasValue)return failure.Value;
            if(c.Kind==PlayableCommandKind.Attack){RefreshVision();if(!VisibleTarget(OwnerFor(c.PlayerId),c.TargetId))return PlayableCommandStatus.InvalidTarget;}
            else if(c.EntityIds.Any(id=>!Geometry.IsFree(c.Target,PlayableUnitRules.Radius(profile,units[id].Kind))))return PlayableCommandStatus.InvalidTarget;
            var accepted=c.EntityIds.Where(id=>!tacticalQueues.TryGetValue(id,out var q)||q.Deferred.Count<profile.UnitOrderQueueLimit).ToArray();
            if(accepted.Length==0)return PlayableCommandStatus.Applied;
            return AcceptPreparedAppend(c,accepted);
        }
        private void ActivateDeferredOrders()
        {
            // Each loop consumes at least one dead-target element. No routing/reservation work for a deferred tail.
            while(true){
                var eligible=tacticalQueues.Where(x=>units.ContainsKey(x.Key)&&x.Value.Active==null&&x.Value.Pending==null&&x.Value.Deferred.Count>0&&
                    !navigation.IsPending(x.Key)&&(!TryActor(x.Key,out var n)||!n.Moving)&&units[x.Key].CurrentOrder==null).OrderBy(x=>x.Key).ToArray();
                if(eligible.Length==0)return;bool skipped=false;
                foreach(var cohort in eligible.GroupBy(x=>x.Value.Deferred[0].Id)){
                    var i=cohort.First().Value.Deferred[0];var ids=cohort.Select(x=>x.Key).ToArray();
                    if(i.Command.Kind==PlayableCommandKind.Attack&&!TargetAlive(i.Command.TargetId)){
                        if(CommitInternalTactical(i,ids,InternalTacticalKind.Skipped))skipped=true;
                        continue;
                    }
                    CommitInternalTactical(i,ids,InternalTacticalKind.Deferred);
                }
                if(!skipped)return;
            }
        }
        private static PlayableCommand RecipientCommand(PlayableCommand c,int[] ids)
        {var copy=new PlayableCommand(c.Generation,c.Sequence,c.PlayerId,c.Kind,ids,c.Target,targetId:c.TargetId,origin:c.Origin,source:c.Source,jobId:c.JobId,actionId:c.ActionId);return c.TargetLocation.HasValue?copy.WithResolvedLocation(c.TargetLocation.Value):copy;}
        private void ActivateTacticalRoute(NavigationRequest r)
        {
            if(!tacticalQueues.TryGetValue(r.Entity,out var q))return;var e=Matches(q.Active,r)?q.Active:Matches(q.Pending,r)?q.Pending:null;if(e==null)return;
            if(e.Interrupted){e.OverrideLocation=DomainLocation(r.GoalLocation);e.AttemptLocation=DomainLocation(r.GoalLocation);e.AttemptRequest=r.Request;e.AttemptOrder=r.Order;if(ReferenceEquals(e,q.Pending)){q.Pending.Failed=true;q.Active=null;units[r.Entity].CurrentOrder=null;units[r.Entity].HasAttackMove=false;units[r.Entity].Target=0;}return;}
            if(e.Issuance.Command.Kind==PlayableCommandKind.Attack&&ReferenceEquals(e,q.Active)){e.AttemptLocation=DomainLocation(r.GoalLocation);e.RouteRequest=r.Request;e.RouteOrder=r.Order;InstallExecution(units[r.Entity],e);return;}
            if(!e.Assigned.Equals(r.GoalLocation))return;
            e.Failed=false;if(e.ActivationTick<0)e.ActivationTick=Tick;e.AttemptLocation=DomainLocation(r.GoalLocation);e.RouteRequest=r.Request;e.RouteOrder=r.Order;q.Active=e;q.Pending=null;q.OldMotionSuspended=false;InstallExecution(units[r.Entity],e);
        }
        private void ObserveTacticalRequest(NavigationRequest r,bool combat=false)
        {
            if(!tacticalQueues.TryGetValue(r.Entity,out var q))return;var e=Matches(q.Active,r)?q.Active:Matches(q.Pending,r)?q.Pending:null;if(e==null)return;
            bool sameAttackChase=e.Issuance.Command.Kind==PlayableCommandKind.Attack&&e.AttemptLocation.HasValue&&e.AttemptLocation.Value.Equals(r.GoalLocation);
            e.TerminalFailure=null;e.AttemptRequest=r.Request;e.AttemptOrder=r.Order;e.AttemptLocation=DomainLocation(r.GoalLocation);e.Failed=false;
            if(e.Issuance.Command.Kind==PlayableCommandKind.Attack&&(combat||combatRequestScope||sameAttackChase))return;
            if(!e.Assigned.Equals(r.GoalLocation)){e.Interrupted=true;e.Arrived=false;e.Suspended=false;e.OverrideLocation=DomainLocation(r.GoalLocation);}
        }
        private static bool Matches(QueueExecution e,NavigationRequest r)=>e!=null&&r.MemberIdentity!=null&&r.MemberIdentity.GroupId==e.Issuance.GroupId&&r.MemberIdentity.OrderRevision==e.Stamp.Revision&&r.MemberIdentity.Incarnation==e.Incarnation;
        private bool PreservedAttackAfterFailedReplacement(Unit u,GroupOrderState group)
        {
            if(!tacticalQueues.TryGetValue(u.Id,out var q)||q.Active?.Issuance.Command.Kind!=PlayableCommandKind.Attack||q.Pending==null||!q.Pending.Failed||!TryActor(u.Id,out var actor)||!TerminalProofMatches(q.Pending,actor))return false;
            var active=q.Active;var member=group.Members.SingleOrDefault(m=>m.Entity==u.Id);return member!=null&&group.GroupId==active.Issuance.GroupId&&group.CommandSequence==active.Issuance.Command.Sequence&&member.OrderRevision==active.Stamp.Revision&&SameSpatialOrder(u.LastOrder,q.Pending.Stamp);
        }
        private void RecordTacticalRouteFailure(NavigationRequest r,NavigationOutcome outcome)
        {
            if(!tacticalQueues.TryGetValue(r.Entity,out var q))return;var e=Matches(q.Pending,r)?q.Pending:Matches(q.Active,r)?q.Active:null;if(e==null)return;
            e.AttemptRequest=r.Request;e.AttemptOrder=r.Order;e.AttemptLocation=DomainLocation(r.GoalLocation);e.TerminalFailure=new TerminalRouteFailure{Request=r.Request,Order=r.Order,GroupId=r.MemberIdentity.GroupId,Revision=r.MemberIdentity.OrderRevision,Incarnation=r.MemberIdentity.Incarnation,Location=DomainLocation(r.GoalLocation),Outcome=outcome,Tick=Tick};
            if(ReferenceEquals(e,q.Pending))e.Failed=true;
        }
        private bool RetainsInstalledExecution(int id)=>tacticalQueues.TryGetValue(id,out var q)&&(q.Active!=null||units.TryGetValue(id,out var u)&&u.CurrentOrder!=null);
        private void QueueMovementCancelled(int id,bool pending){if(!tacticalQueues.TryGetValue(id,out var q)||pending)return;if(q.Pending!=null)q.Pending.Failed=true;else if(q.Active?.Interrupted==true)q.Active.Failed=true;}
        private bool CanResumeCombat(int id)=>!navigation.IsPending(id)||tacticalQueues.TryGetValue(id,out var q)&&(q.Active?.Suspended==true||q.OldMotionSuspended);
        private bool HasQueueExecution(int id)=>tacticalQueues.TryGetValue(id,out var q)&&(q.Active!=null&&!q.Active.Interrupted||q.Pending!=null);
        private bool TargetAlive(int id)=>units.TryGetValue(id,out var u)&&u.Health>0||buildings.TryGetValue(id,out var b)&&b.Health>0;
        private bool AnchorOccupied(Unit u,QueueExecution e)
        {
            var anchor=e.Issuance.Command.TargetLocation;if(!anchor.HasValue)return false;
            double radius=PlayableUnitRules.Vision(profile,u.Kind);
            foreach(int id in units.Keys.Concat(buildings.Keys)){
                if(!VisibleTarget(u.Owner,id))continue; // Never read a hidden live position.
                if(!Target(id,out var p,out _)||Distance(anchor.Value.Position,p)>radius)continue;
                NavLocation other;if(units.ContainsKey(id)){if(!TryActor(id,out var visibleActor))continue;other=visibleActor.Location;}else if(!navigation.Crowd.Locate(p,0,out other))continue;
                if(profile.AuthoredMap==null||profile.AuthoredMap.CompatibleCombatSurface(anchor.Value,other))return true;
            }
            return false;
        }
        private void ParkFailedReplacement(Unit u,TacticalQueue q)
        {
            var e=q.Pending;CommitInternalTactical(e.Issuance,new[]{u.Id},InternalTacticalKind.Park,activationRevision:e.Stamp.Revision);
        }
        private void InvalidateTacticalArrivalAfterGeometryChange()
        {
            foreach(var row in tacticalQueues)
                if(row.Value.Active is QueueExecution e&&e.Arrived&&TryActor(row.Key,out var actor)&&actor.Outcome!=NavigationOutcome.Arrived)e.Arrived=false;
        }
        private void RefreshTacticalQueues()
        {
            foreach(var row in tacticalQueues.ToArray()){
                if(!units.TryGetValue(row.Key,out var u)||eliminated.Contains(u.Owner)){tacticalQueues.Remove(row.Key);continue;}
                var q=row.Value;var e=q.Active;if(e==null||!TryActor(u.Id,out var actor))continue;
                if(e.Interrupted&&e.Issuance.Command.Kind!=PlayableCommandKind.Attack)continue;var kind=e.Issuance.Command.Kind;
                if(kind!=PlayableCommandKind.Attack&&e.ActivationTick>=0&&actor.Outcome==NavigationOutcome.Arrived&&e.Assigned.HasValue&&actor.GoalLocation.Equals(e.Assigned)&&
                    navigation.Crowd.CompatibleArrival(actor.Location,e.Assigned.Value,actor.Radius))e.Arrived=true;
                bool complete=kind==PlayableCommandKind.Attack?!TargetAlive(e.Issuance.Command.TargetId):e.Arrived&&(kind==PlayableCommandKind.Move||!AnchorOccupied(u,e));
                if(!complete)continue;
                if(q.Pending?.Failed==true&&q.Pending.TerminalFailure!=null&&kind==PlayableCommandKind.Attack){ParkFailedReplacement(u,q);continue;}
                if(q.Pending==null)q.Completion=new PlayableTacticalCompletion(e.Stamp,e.Issuance.Id,e.Issuance.GroupId,e.Incarnation,e.ActivationTick,Tick,e.Issuance.Command.TargetLocation,e.Assigned,e.Issuance.Command.TargetId);
                q.Active=null;u.CurrentOrder=null;u.HasAttackMove=false;u.Target=0;u.ExplicitTarget=false;InterruptBurst(u);
                if(q.Pending==null&&kind==PlayableCommandKind.Attack)navigation.Stop(u.Id,false);
            }
            ActivateDeferredOrders();
        }
        private void CombatStop(int id)
        {
            if(tacticalQueues.TryGetValue(id,out var q)&&(q.Pending!=null||q.Active?.Issuance.Command.Kind==PlayableCommandKind.AttackMove)){
                if(q.Active!=null)q.Active.Suspended=true;else q.OldMotionSuspended=true;navigation.Crowd.Suspend(id);return;
            }
            if(tacticalQueues.TryGetValue(id,out var current)&&current.Active?.Issuance.Command.Kind==PlayableCommandKind.Attack&&TryActor(id,out var actor)&&!actor.Moving&&!navigation.IsPending(id))return;
            bool cancelledChase=current?.Active?.Issuance.Command.Kind==PlayableCommandKind.Attack&&navigation.IsPending(id);
            navigation.Stop(id,false);
            // Stop removes the pending chase. Its attempt cannot remain the
            // active proof after cancellation; the last installed route (or no
            // route yet) is still the execution's authoritative binding.
            if(cancelledChase&&!navigation.IsPending(id)&&TryActor(id,out var stopped)&&!stopped.Moving){
                var e=current.Active;
                if(e.RouteRequest==0||stopped.InstalledRequest==e.RouteRequest&&stopped.InstalledOrder==e.RouteOrder&&stopped.InstalledGroupId==e.Issuance.GroupId&&stopped.InstalledRevision==e.Stamp.Revision){
                    e.AttemptRequest=e.RouteRequest;e.AttemptOrder=e.RouteOrder;e.AttemptLocation=e.RouteRequest==0?null:DomainLocation(stopped.GoalLocation);e.TerminalFailure=null;
                }
            }
        }
        private bool CombatMove(int id,NavPoint goal)
        {
            if(!CampaignMoveAllowed(id,goal))return false;
            if(tacticalQueues.TryGetValue(id,out var q)){
                if(q.Active!=null&&q.Active.Suspended&&(q.Active.Assigned.HasValue&&q.Active.Assigned.Value.Position.Equals(goal)||q.Active.Issuance.Command.Kind==PlayableCommandKind.Attack)){
                    q.Active.Suspended=false;return navigation.Crowd.Resume(id);
                }
                if(q.OldMotionSuspended){q.OldMotionSuspended=false;return navigation.Crowd.Resume(id);}
                    if(q.Pending!=null){
                    if(q.Pending.Failed&&q.Pending.TerminalFailure!=null&&q.Active?.Issuance.Command.Kind==PlayableCommandKind.Attack&&TargetAlive(q.Active.Issuance.Command.TargetId)){
                        return CommitInternalTactical(q.Active.Issuance,new[]{id},InternalTacticalKind.Combat,goal,q.Active.Stamp.Revision);
                    }
                    return false;
                }
            }
            combatRequestScope=true;try{return navigation.Move(id,goal);}finally{combatRequestScope=false;}
        }
        private bool PendingAiRouteViable(int id,QueueExecution pending)
        {
            var command=pending?.Issuance.Command;
            bool attackMove=command?.Kind==PlayableCommandKind.AttackMove&&command.SiteId>0;
            bool move=command?.Kind==PlayableCommandKind.Move&&command.SiteId==0;
            if(pending==null||pending.Failed||pending.Interrupted||pending.TerminalFailure!=null||pending.ActivationTick>=0||
                (!attackMove&&!move)||command.Origin!=PlayableOrderOrigin.Ai||
                command.Source!=PlayableAiOpeningComposition.SourceIdentity||!pending.Assigned.HasValue)return false;
            var request=navigation.PendingRequestFor(id);
            return request!=null&&Matches(pending,request)&&request.Request==pending.AttemptRequest&&request.Order==pending.AttemptOrder&&
                pending.AttemptLocation.HasValue&&pending.AttemptLocation.Value.Equals(request.GoalLocation)&&pending.Assigned.Value.Equals(request.GoalLocation);
        }
        internal PlayableUnitQueueSnapshot TacticalQueueProjection(int id,PlayableOwner observer)
        {
            if(!units.TryGetValue(id,out var u)||u.Owner!=observer||!tacticalQueues.TryGetValue(id,out var q))return null;
            PlayableQueuedOrderSnapshot Project(QueueIssuance i,long revision)=>new PlayableQueuedOrderSnapshot(i.Id,i.GroupId,i.Command.Sequence,revision,i.Command.Kind,i.Command.TargetLocation,
                i.Command.Kind==PlayableCommandKind.Attack&&VisibleTarget(observer,i.Command.TargetId)?i.Command.TargetId:0,
                i.Command.Kind==PlayableCommandKind.AttackMove?i.Command.SiteId:0,
                i.Command.Origin==PlayableOrderOrigin.Ai&&i.Command.Source==PlayableAiOpeningComposition.SourceIdentity?i.Command.ActionId:0);
            return new PlayableUnitQueueSnapshot(q.Active==null?null:Project(q.Active.Issuance,q.Active.Stamp.Revision),q.Pending==null?null:Project(q.Pending.Issuance,q.Pending.Stamp.Revision),q.Deferred.Select(i=>Project(i,0)).ToArray(),q.Completion,PendingAiRouteViable(id,q.Pending));
        }
    }
}

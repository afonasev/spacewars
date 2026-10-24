using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Preserve every secondary fault without replacing the original fatal error.
        internal static void PreserveTacticalSecondary(Exception primary,Exception secondary)
        {
            if(ReferenceEquals(primary,secondary))return;
            if(!(primary.Data["TacticalSecondaryFailures"] is List<Exception> errors)){errors=new List<Exception>();primary.Data["TacticalSecondaryFailures"]=errors;}
            errors.Add(secondary);
        }
        internal static string TacticalFailureText(Exception primary)
        {
            string text=primary.ToString();if(primary.Data["TacticalSecondaryFailures"] is List<Exception> errors)
                foreach(var error in errors)text+="\nSecondary tactical failure:\n"+TacticalFailureText(error);
            return text;
        }
        private void AbortTacticalPreserving(Exception primary){try{navigation.AbortTacticalPreparation();}catch(Exception cleanup){PreserveTacticalSecondary(primary,cleanup);}}
        internal enum TacticalCommitPhase { NotCommitted,Committing,Committed }
        internal sealed class TacticalCommitFailure : Exception
        {
            internal readonly TacticalCommitPhase Phase;internal readonly Exception Original;
            private static Exception Root(Exception e){while(e is TacticalCommitFailure f)e=f.Original;return e;}
            internal TacticalCommitFailure(TacticalCommitPhase phase,Exception original):base(Root(original).Message,Root(original)){Phase=phase;Original=Root(original);}
        }
        internal Action<PreparedTacticalCommit> AfterTacticalPreparationForQa;
        internal bool MeasureTacticalCommitForQa;internal long LastTacticalCommitAllocatedBytesForQa;
        private PlayableCommandStatus ApplyTacticalPrepared(PlayableCommand command,out string message,out bool committed)
        {
            message="Tactical preparation refused.";committed=false;
            PreparedTacticalCommit payload=null;Exception primary=null;
            try{
                try{payload=PreparedTacticalCommit.Create(this);}catch(InvalidOperationException e)when(e.Message=="Physical storage exceeds prepared reservation."){return PlayableCommandStatus.Overflow;}
                if(payload==null)return PlayableCommandStatus.Overflow;
                navigation.PrepareCommandGroup(command,nextOrderRevision,Tick,TeamOf(OwnerFor(command.PlayerId)));payload.CaptureNavigationInputs();
                HashSet<int> followed=null;PlayableCommandStatus status;
                try{status=command.Kind==PlayableCommandKind.Follow?Follow(command.CopyEntityIds(),command.TargetId,command.Sequence,OwnerFor(command.PlayerId),out message,out followed,payload):ApplyCore(command,out message,payload);}
                finally{payload.CaptureProposalAndRestore();}
                if(status!=PlayableCommandStatus.Applied){navigation.AbortTacticalPreparation();return status;}
                var ids=(command.Kind==PlayableCommandKind.Follow?(IEnumerable<int>)followed:command.EntityIds).Distinct().OrderBy(id=>id).ToArray();
                var candidate=navigation.PreparedTacticalCandidate;
                if(candidate==null)throw new InvalidOperationException("Tactical command did not stage its Nav effects.");
                payload.PrepareAcceptance(candidate,command,ids);AfterTacticalPreparationForQa?.Invoke(payload);
                if(!navigation.FinishTacticalPreparation(payload,ids)){message="Tactical readset changed before commit.";return PlayableCommandStatus.Rejected;}
                committed=true;return PlayableCommandStatus.Applied;
            }catch(Exception error){
                primary=error;
                if(payload!=null&&payload.Phase!=TacticalCommitPhase.NotCommitted)throw new TacticalCommitFailure(payload.Phase,error);
                AbortTacticalPreserving(error);throw;
            }finally{
                if(payload!=null){if(payload.Phase==TacticalCommitPhase.NotCommitted)payload.RejectPreparation();try{payload.Dispose();}catch(Exception cleanup){if(primary!=null)PreserveTacticalSecondary(primary is TacticalCommitFailure f?f.Original:primary,cleanup);else if(payload.Phase!=TacticalCommitPhase.NotCommitted)throw new TacticalCommitFailure(payload.Phase,cleanup);else throw;}}
            }
        }
        private enum InternalTacticalKind { Deferred,Skipped,Park,Combat }
        private bool CommitInternalTactical(QueueIssuance original,int[] ids,InternalTacticalKind kind,NavPoint goal=default,long activationRevision=0)
        {
            var command=RecipientCommand(original.Command,ids);long revision=kind==InternalTacticalKind.Deferred||kind==InternalTacticalKind.Skipped?nextOrderRevision:activationRevision>0?activationRevision:original.Revision;
            PreparedTacticalCommit payload=null;Exception primary=null;
            try{
                try{payload=PreparedTacticalCommit.Create(this);}catch(InvalidOperationException e)when(e.Message=="Physical storage exceeds prepared reservation."){return false;}if(payload==null)return false;
                navigation.PrepareCommandGroup(command,revision,Tick,TeamOf(units[ids[0]].Owner),original.GroupId,original.Revision,original.Tick);payload.CaptureNavigationInputs();
                bool staged;
                if(kind==InternalTacticalKind.Skipped){navigation.StageTacticalMetadata(ids,payload.NavigationInputs);staged=payload.BeginProposal();}
                else if(kind==InternalTacticalKind.Combat){
                    // Rebind the original active issuance context, not the failed replacement's legacy membership.
                    staged=navigation.StageTacticalMove(ids,new[]{goal},payload.NavigationInputs);if(staged)staged=payload.BeginProposal();
                }
                else if(kind==InternalTacticalKind.Park||command.Kind==PlayableCommandKind.Attack){
                    staged=navigation.StageTacticalStop(ids,false,payload.NavigationInputs);if(staged)staged=payload.BeginProposal();
                    if(staged)foreach(int id in ids){var u=units[id];InterruptBurst(u);u.Target=kind==InternalTacticalKind.Park?0:command.TargetId;u.ExplicitTarget=kind!=InternalTacticalKind.Park;u.HasAttackMove=false;u.CurrentOrder=kind==InternalTacticalKind.Park?null:Order(u,original);}
                }else staged=ApplyCore(command,out _,payload)==PlayableCommandStatus.Applied;
                payload.CaptureProposalAndRestore();
                if(!staged){navigation.AbortTacticalPreparation();return false;}
                var candidate=navigation.PreparedTacticalCandidate;if(candidate==null)throw new InvalidOperationException("Internal tactical operation did not stage Nav.");
                payload.PrepareResult(command,ids,()=>{
                    QueueIssuance issuance=null;
                    if(kind==InternalTacticalKind.Deferred||kind==InternalTacticalKind.Skipped){
                        var first=tacticalQueues[ids[0]];if(first.Deferred.Count==0||first.Deferred[0].Id!=original.Id)throw new InvalidOperationException("Deferred source issuance changed.");
                        issuance=first.Deferred[0];issuance.GroupId=candidate.Groups[candidate.MemberGroups[ids[0]]].GroupId;nextOrderRevision=revision+1;
                    }
                    foreach(int id in ids){
                        var u=units[id];var q=tacticalQueues[id];
                        if(kind==InternalTacticalKind.Combat){foreach(var request in candidate.Issued)ObserveSpatialRequest(request,true);break;}
                        InvalidateSpatialCompletion(id);
                        if(kind==InternalTacticalKind.Park){q.Active=null;u.CurrentOrder=null;u.HasAttackMove=false;u.Target=0;u.ExplicitTarget=false;CancelSpatialExecution(id,candidate.Pending.ContainsKey(id)||candidate.Allocating.ContainsKey(id));continue;}
                        if(q.Deferred.Count==0||q.Deferred[0].Id!=original.Id)throw new InvalidOperationException("Deferred source issuance changed.");
                        q.Deferred.RemoveAt(0);u.LastOrder=Stamp(u,issuance,revision);
                        if(kind==InternalTacticalKind.Skipped){q.Completion=new PlayableTacticalCompletion(u.LastOrder,issuance.Id,issuance.GroupId,TryActor(id,out var actor)?actor.Incarnation:0,Tick,Tick,targetId:command.TargetId);continue;}
                        q.Completion=null;var member=candidate.Groups[candidate.MemberGroups[id]].Members.Single(m=>m.Entity==id);
                        var execution=new QueueExecution{Issuance=issuance,Stamp=u.LastOrder,Incarnation=member.Incarnation,Assigned=DomainLocation(member.GoalLocation)};
                        if(command.Kind==PlayableCommandKind.Attack){execution.ActivationTick=Tick;q.Active=execution;q.Pending=null;InstallExecution(u,execution);CancelSpatialExecution(id,false);}
                        else{var request=candidate.Pending.TryGetValue(id,out var pending)?pending:candidate.Allocating[id];execution.AttemptRequest=request.Request;execution.AttemptOrder=request.Order;execution.AttemptLocation=DomainLocation(request.GoalLocation);q.Pending=execution;AdmitSpatialExecution(u,candidate);u.CurrentOrder=null;u.Target=0;u.HasAttackMove=false;}
                    }
                });
                AfterTacticalPreparationForQa?.Invoke(payload);return navigation.FinishTacticalPreparation(payload,ids);
            }catch(Exception error){primary=error;if(payload!=null&&payload.Phase!=TacticalCommitPhase.NotCommitted)throw new TacticalCommitFailure(payload.Phase,error);AbortTacticalPreserving(error);throw;}
            finally{if(payload!=null){payload.RejectPreparation();try{payload.Dispose();}catch(Exception cleanup){if(primary!=null)PreserveTacticalSecondary(primary is TacticalCommitFailure f?f.Original:primary,cleanup);else if(payload.Phase!=TacticalCommitPhase.NotCommitted)throw new TacticalCommitFailure(payload.Phase,cleanup);else throw;}}}
        }
        private PlayableCommandStatus AcceptPreparedAppend(PlayableCommand command,int[] ids)
        {
            PreparedTacticalCommit payload=null;Exception primary=null;
            try{
                try{payload=PreparedTacticalCommit.Create(this);}catch(InvalidOperationException e)when(e.Message=="Physical storage exceeds prepared reservation."){return PlayableCommandStatus.Overflow;}
                if(payload==null)return PlayableCommandStatus.Overflow;
                if(!payload.BeginProposal())return PlayableCommandStatus.Rejected;payload.CaptureProposalAndRestore();
                payload.PrepareResult(command,ids,()=>{var issuance=NewIssuance(command,nextOrderRevision++);foreach(int id in ids)QueueFor(id).Deferred.Add(issuance);});
                AfterTacticalPreparationForQa?.Invoke(payload);
                try{payload.CommitDomainOnly();}catch(Exception error)when(payload.Phase==TacticalCommitPhase.NotCommitted&&(NavigationCommandRefused.IsExpected(error)||error is OperationCanceledException&&payload.IsCancelled)){return PlayableCommandStatus.Rejected;}
                payload.Dispose();ActivateDeferredOrders();return PlayableCommandStatus.Applied;
            }catch(Exception error){primary=error;if(payload!=null&&payload.Phase!=TacticalCommitPhase.NotCommitted)throw new TacticalCommitFailure(payload.Phase,error);throw;}
            finally{if(payload!=null){payload.RejectPreparation();try{payload.Dispose();}catch(Exception cleanup){if(primary!=null)PreserveTacticalSecondary(primary is TacticalCommitFailure f?f.Original:primary,cleanup);else if(payload.Phase!=TacticalCommitPhase.NotCommitted)throw new TacticalCommitFailure(payload.Phase,cleanup);else throw;}}}
        }
        // Concrete transient preparation, paid until Finish. It is not world state,
        // a second writer, a wire extension or a callback inside the commit gate.
        internal sealed class PreparedTacticalCommit : NavOwnedStorage,IDisposable
        {
            private struct UnitValues
            {
                internal Unit Unit;internal TacticalBehavior Behavior;internal PlayableOrderStamp Stamp;internal double Health;internal PlayableOwner Owner;internal PlayableEntityKind Kind;
                internal void Install(){Behavior.Install(Unit);Unit.LastOrder=Stamp;}
            }
            private struct BuildingInput {internal Building Owner;internal double Health;internal PlayableOwner Faction;internal PlayableBuildingKind Kind;internal NavPoint Position;internal bool Ready;}
            private readonly BuildingInput[] buildings;private readonly PlayableMatchOutcome outcome;
            private readonly PlayableDomain domain;
            private readonly NavigationRetentionBudget.Scope preparation;
            internal NavigationSession.MovementReadSet NavigationInputs {get;private set;}
            private readonly UnitValues[] before;
            private UnitValues[] after;
            private readonly long tick,revision,queue;
            private readonly PlayableProfile profile;private readonly NavGeometry domainGeometry;private readonly int domainGeometryRevision;
            private readonly AuthoritySlotMap<int,TacticalQueue> sourceQueues,queueReadset;
            private readonly AuthoritySlotMap<int,SpatialExecution> sourceExecutions,executionReadset;
            private readonly AuthoritySlotMap<int,PlayableSpatialCompletion> sourceCompletions,completionReadset;
            private readonly int[] queueLayout,executionLayout,completionLayout;
            private AuthoritySlotMap<int,TacticalQueue> nextQueues;
            private AuthoritySlotMap<int,SpatialExecution> nextExecutions;
            private AuthoritySlotMap<int,PlayableSpatialCompletion> nextCompletions;
            private long nextRevision,nextQueue;
            private bool released,ready,proposalOwned;private readonly bool measure;private long allocatedStart;private bool allocationMeterArmed;
            internal TacticalCommitPhase Phase {get;private set;}
            internal NavigationCommandRefused Refusal {get;set;}
            internal bool IsCancelled=>preparation.IsCancelled;
            internal void NotifyExternalRequest(Action<NavigationRequest> observers,NavigationRequest request)
            {var external=observers-domain.ObserveSpatialRequest;external?.Invoke(request);}
            internal void NotifyExternalCancellation(Action<int> observers,int entity)
            {var external=observers-domain.CancelSpatialExecution;external?.Invoke(entity);}
            internal int[] Recipients {get;private set;}
            internal PlayableCommand Command {get;private set;}
            internal override bool IsImmutableCaptureRoot=>false;
            private PreparedTacticalCommit(PlayableDomain d,NavigationRetentionBudget.Scope scope,long bytes):base(bytes)
            {
                domain=d;preparation=scope;measure=d.MeasureTacticalCommitForQa;tick=d.Tick;revision=d.nextOrderRevision;queue=d.queueSequence;profile=d.profile;domainGeometry=d.Geometry;domainGeometryRevision=d.Geometry.Revision;
                buildings=new BuildingInput[d.buildings.Count];int bi=0;foreach(var b in d.buildings.Values)buildings[bi++]=new BuildingInput{Owner=b,Health=b.Health,Faction=b.Owner,Kind=b.Kind,Position=b.Position,Ready=b.Ready};outcome=d.Outcome;
                before=CaptureUnits(d);sourceQueues=d.tacticalQueues;sourceExecutions=d.spatialExecutions;sourceCompletions=d.spatialCompletions;
                queueReadset=CopyQueues(sourceQueues);executionReadset=Copy(sourceExecutions,CloneExecution);completionReadset=Copy(sourceCompletions,null);
                queueLayout=sourceQueues.CaptureLayout();executionLayout=sourceExecutions.CaptureLayout();completionLayout=sourceCompletions.CaptureLayout();Retain();
            }
            internal static PreparedTacticalCommit Create(PlayableDomain d)
            {
                d.ValidateDomainLocationValues();
                var n=d.navigation;if(!n.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,n.Generation,n.Admission.PublicationIdentity,out var scope))return null;
                PreparedTacticalCommit value=null;
                try{
                    // Conservative CLR peak bound: before/after unit snapshots,
                    // two complete queue/execution copies including deferred tails,
                    // map backing/holes/layout arrays and all acceptance DTOs.
                    long bytes=checked(4096L+4096L*d.units.Count+256L*d.buildings.Count+2048L*(d.tacticalQueues.SlotCount+d.spatialExecutions.SlotCount+d.spatialCompletions.SlotCount)+1024L*d.profile.UnitOrderQueueLimit*d.tacticalQueues.Count);
                    using(var a=new NavStorageAllocation(scope.TryReserve)){a.Reserve(bytes);value=a.Register(new PreparedTacticalCommit(d,scope,bytes));scope.Attach(value);return value;}
                }catch{value?.ReleaseOwner();scope.Dispose();throw;}
            }
            private static UnitValues[] CaptureUnits(PlayableDomain d)
            {var a=new UnitValues[d.units.Count];int i=0;foreach(var u in d.units.Values)a[i++]=new UnitValues{Unit=u,Behavior=TacticalBehavior.Capture(u),Stamp=u.LastOrder,Health=u.Health,Owner=u.Owner,Kind=u.Kind};return a;}
            private static AuthoritySlotMap<int,T> Copy<T>(AuthoritySlotMap<int,T> source,Func<T,T> clone)
            {var result=new AuthoritySlotMap<int,T>();foreach(var row in source)result.Add(row.Key,clone==null?row.Value:clone(row.Value));result.RestoreLayout(source.CaptureLayout());return result;}
            private static QueueIssuance CloneIssuance(QueueIssuance i)=>i==null?null:new QueueIssuance{Id=i.Id,Revision=i.Revision,Tick=i.Tick,GroupId=i.GroupId,Command=i.Command};
            private static TerminalRouteFailure CloneFailure(TerminalRouteFailure f)=>f==null?null:new TerminalRouteFailure{Request=f.Request,Order=f.Order,GroupId=f.GroupId,Revision=f.Revision,Incarnation=f.Incarnation,Tick=f.Tick,Location=f.Location,Outcome=f.Outcome};
            private static QueueExecution CloneQueueExecution(QueueExecution e,Func<QueueIssuance,QueueIssuance> issuance)=>e==null?null:new QueueExecution{Issuance=issuance(e.Issuance),Stamp=e.Stamp,Assigned=e.Assigned,Incarnation=e.Incarnation,ActivationTick=e.ActivationTick,RouteRequest=e.RouteRequest,RouteOrder=e.RouteOrder,Arrived=e.Arrived,Suspended=e.Suspended,Failed=e.Failed,Interrupted=e.Interrupted,OverrideLocation=e.OverrideLocation,AttemptLocation=e.AttemptLocation,AttemptRequest=e.AttemptRequest,AttemptOrder=e.AttemptOrder,TerminalFailure=CloneFailure(e.TerminalFailure)};
            private static TacticalQueue CloneQueue(TacticalQueue q,Func<QueueIssuance,QueueIssuance> issuance)
            {var n=new TacticalQueue{Active=CloneQueueExecution(q.Active,issuance),Pending=CloneQueueExecution(q.Pending,issuance),Completion=q.Completion,OldMotionSuspended=q.OldMotionSuspended};foreach(var i in q.Deferred)n.Deferred.Add(issuance(i));return n;}
            private static AuthoritySlotMap<int,TacticalQueue> CopyQueues(AuthoritySlotMap<int,TacticalQueue> source)
            {
                // Issuance identity is shared across members and across deferred /
                // active phases; its GroupId must advance once for the whole cohort.
                var aliases=new Dictionary<QueueIssuance,QueueIssuance>();
                QueueIssuance Clone(QueueIssuance i){if(i==null)return null;if(!aliases.TryGetValue(i,out var copy)){copy=CloneIssuance(i);aliases.Add(i,copy);}return copy;}
                return Copy(source,q=>CloneQueue(q,Clone));
            }
            private static SpatialExecution CloneExecution(SpatialExecution e)=>new SpatialExecution{Order=e.Order,OriginalTarget=e.OriginalTarget,AssignedGoal=e.AssignedGoal,OriginalLocation=e.OriginalLocation,AssignedLocation=e.AssignedLocation,GroupId=e.GroupId,Incarnation=e.Incarnation,ActivationTick=e.ActivationTick,RouteRequest=e.RouteRequest,RouteOrder=e.RouteOrder};
            private static bool PointSame(NavPoint a,NavPoint b)=>a.X.Equals(b.X)&&a.Z.Equals(b.Z);
            private static bool BehaviorSame(TacticalBehavior a,Unit u)=>ReferenceEquals(a.Order,u.CurrentOrder)&&a.Target==u.Target&&a.Explicit==u.ExplicitTarget&&a.AttackMove==u.HasAttackMove&&PointSame(a.Anchor,u.AttackMove)&&a.Repath==u.Repath&&a.Stopped==u.StoppedSeconds&&a.Reload==u.Reload&&a.BurstRemaining==u.BurstRemaining&&a.BurstTarget==u.BurstTarget&&a.BurstSequence==u.BurstSequence&&a.BurstSize==u.BurstSize&&a.BurstSpread==u.BurstSpread&&a.BurstDelay==u.BurstDelay;
            private static bool IssuanceSame(QueueIssuance a,QueueIssuance b)=>ReferenceEquals(a,b)||a!=null&&b!=null&&a.Id==b.Id&&a.Revision==b.Revision&&a.Tick==b.Tick&&a.GroupId==b.GroupId&&ReferenceEquals(a.Command,b.Command);
            private static bool LocationSame(NavLocation? a,NavLocation? b)=>a.HasValue==b.HasValue&&(!a.HasValue||PointSame(a.Value.Position,b.Value.Position)&&a.Value.SurfaceId==b.Value.SurfaceId&&ReferenceEquals(a.Value.NameReference.Owner,b.Value.NameReference.Owner)&&a.Value.NameReference.Role==b.Value.NameReference.Role&&a.Value.NameReference.Index==b.Value.NameReference.Index);
            private static bool FailureSame(TerminalRouteFailure a,TerminalRouteFailure b)=>ReferenceEquals(a,b)||a!=null&&b!=null&&a.Request==b.Request&&a.Order==b.Order&&a.GroupId==b.GroupId&&a.Revision==b.Revision&&a.Incarnation==b.Incarnation&&a.Tick==b.Tick&&LocationSame(a.Location,b.Location)&&a.Outcome==b.Outcome;
            private static bool ExecutionSame(QueueExecution a,QueueExecution b)=>ReferenceEquals(a,b)||a!=null&&b!=null&&IssuanceSame(a.Issuance,b.Issuance)&&ReferenceEquals(a.Stamp,b.Stamp)&&LocationSame(a.Assigned,b.Assigned)&&a.Incarnation==b.Incarnation&&a.ActivationTick==b.ActivationTick&&a.RouteRequest==b.RouteRequest&&a.RouteOrder==b.RouteOrder&&a.Arrived==b.Arrived&&a.Suspended==b.Suspended&&a.Failed==b.Failed&&a.Interrupted==b.Interrupted&&LocationSame(a.OverrideLocation,b.OverrideLocation)&&LocationSame(a.AttemptLocation,b.AttemptLocation)&&a.AttemptRequest==b.AttemptRequest&&a.AttemptOrder==b.AttemptOrder&&FailureSame(a.TerminalFailure,b.TerminalFailure);
            private static bool QueueSame(TacticalQueue a,TacticalQueue b)
            {if(a==null||b==null||!ExecutionSame(a.Active,b.Active)||!ExecutionSame(a.Pending,b.Pending)||!ReferenceEquals(a.Completion,b.Completion)||a.OldMotionSuspended!=b.OldMotionSuspended||a.Deferred.Count!=b.Deferred.Count)return false;for(int i=0;i<a.Deferred.Count;i++)if(!IssuanceSame(a.Deferred[i],b.Deferred[i]))return false;return true;}
            private static bool SpatialSame(SpatialExecution a,SpatialExecution b)=>a!=null&&b!=null&&ReferenceEquals(a.Order,b.Order)&&PointSame(a.OriginalTarget,b.OriginalTarget)&&PointSame(a.AssignedGoal,b.AssignedGoal)&&LocationSame(a.OriginalLocation,b.OriginalLocation)&&LocationSame(a.AssignedLocation,b.AssignedLocation)&&a.GroupId==b.GroupId&&a.Incarnation==b.Incarnation&&a.ActivationTick==b.ActivationTick&&a.RouteRequest==b.RouteRequest&&a.RouteOrder==b.RouteOrder;
            internal void CaptureNavigationInputs(){var inputs=NavigationSession.MovementReadSet.CreateOwned(domain.navigation,preparation.OwnedReservation());try{NavigationInputs=NavPhysicalMacro.Acquire(this,inputs);}finally{inputs.ReleaseOwner();}}
            internal void RestoreBefore(){for(int i=0;i<before.Length;i++)before[i].Install();}
            internal void CaptureProposalAndRestore(){if(!proposalOwned)return;try{after=CaptureUnits(domain);}finally{RestoreBefore();proposalOwned=false;}}
            internal bool BeginProposal(){if(!InputsCurrent())return false;proposalOwned=true;return true;}
            internal void PrepareAcceptance(NavigationGroupCommand candidate,PlayableCommand command,int[] recipients)
            {
                if(after==null||ready)throw new InvalidOperationException("Missing tactical proposal.");
                PrepareResult(command,recipients,()=>{
                var previous=new Dictionary<int,TacticalBehavior>();for(int i=0;i<before.Length;i++)previous.Add(before[i].Unit.Id,before[i].Behavior);
                long acceptedRevision=domain.nextOrderRevision++;
                for(int i=0;i<recipients.Length;i++){
                    int id=recipients[i];var u=domain.units[id];domain.InvalidateSpatialCompletion(id);
                    u.LastOrder=new PlayableOrderStamp(id,u.Owner,domain.navigation.Generation,acceptedRevision,command.Sequence,domain.Tick,command.Origin,command.Kind,command.Source,command.JobId,command.ActionId);
                    if(u.CurrentOrder!=null&&command.TargetLocation.HasValue){var o=u.CurrentOrder;u.CurrentOrder=new PlayableTacticalOrderSnapshot(o.UnitId,o.Owner,o.Generation,o.CommandSequence,o.IssuedTick,o.Kind,o.Destination,o.TargetId,DomainLocation(command.TargetLocation));}
                    domain.AdmitSpatialExecution(u,candidate);
                }
                domain.RegisterAcceptedQueue(command,acceptedRevision,previous,recipients,candidate);
                if(candidate.IsStop)foreach(int id in recipients)domain.CancelSpatialExecution(id,candidate.Pending.ContainsKey(id)||candidate.Allocating.ContainsKey(id));
                });
            }
            // The action is an internal off-gate calculation, never a commit callback.
            internal void PrepareResult(PlayableCommand command,int[] recipients,Action calculate)
            {
                if(after==null||ready)throw new InvalidOperationException("Missing tactical proposal.");
                Command=command;Recipients=recipients;nextQueues=CopyQueues(sourceQueues);nextExecutions=Copy(sourceExecutions,CloneExecution);nextCompletions=Copy(sourceCompletions,null);
                try{
                    domain.tacticalQueues=nextQueues;domain.spatialExecutions=nextExecutions;domain.spatialCompletions=nextCompletions;
                    for(int i=0;i<after.Length;i++)after[i].Install();
                    calculate();domain.ValidateDomainLocationValues();after=CaptureUnits(domain);nextRevision=domain.nextOrderRevision;nextQueue=domain.queueSequence;ready=true;
                }finally{
                    RestoreBefore();domain.tacticalQueues=sourceQueues;domain.spatialExecutions=sourceExecutions;domain.spatialCompletions=sourceCompletions;domain.nextOrderRevision=revision;domain.queueSequence=queue;
                }
            }
            internal void ValidateBeforeGate()
            {if(!queueLayout.SequenceEqual(sourceQueues.CaptureLayout())||!executionLayout.SequenceEqual(sourceExecutions.CaptureLayout())||!completionLayout.SequenceEqual(sourceCompletions.CaptureLayout()))throw new NavigationCommandRefused("Tactical source layout changed.");ValidateInGate();}
            internal void ValidateInGate()
            {
                preparation.AssertOwnerLive();
                if(!ready||Phase!=TacticalCommitPhase.NotCommitted)throw new InvalidOperationException("Invalid prepared tactical phase.");
                if(preparation.IsCancelled)throw new NavigationCommandRefused("Tactical domain preparation cancelled.",NavigationCommandRefused.Cause.Cancelled);
                if(!InputsCurrent())throw new NavigationCommandRefused("Tactical domain source changed.");
            }
            private bool InputsCurrent()
            {
                if(domain.Outcome!=outcome||domain.buildings.Count!=buildings.Length||preparation.IsCancelled||!ReferenceEquals(domain.profile,profile)||!ReferenceEquals(domain.Geometry,domainGeometry)||domain.Geometry.Revision!=domainGeometryRevision||domain.Tick!=tick||domain.nextOrderRevision!=revision||domain.queueSequence!=queue||domain.units.Count!=before.Length||!ReferenceEquals(domain.tacticalQueues,sourceQueues)||!ReferenceEquals(domain.spatialExecutions,sourceExecutions)||!ReferenceEquals(domain.spatialCompletions,sourceCompletions)||sourceQueues.Count!=queueReadset.Count||sourceExecutions.Count!=executionReadset.Count||sourceCompletions.Count!=completionReadset.Count)return false;
                for(int i=0;i<buildings.Length;i++){var b=buildings[i];if(!domain.buildings.TryGetValue(b.Owner.Id,out var live)||!ReferenceEquals(b.Owner,live)||b.Health!=live.Health||b.Faction!=live.Owner||b.Kind!=live.Kind||!PointSame(b.Position,live.Position)||b.Ready!=live.Ready)return false;}
                for(int i=0;i<before.Length;i++){var u=before[i];if(!domain.units.TryGetValue(u.Unit.Id,out var live)||!ReferenceEquals(live,u.Unit)||!ReferenceEquals(live.LastOrder,u.Stamp)||live.Health!=u.Health||live.Owner!=u.Owner||live.Kind!=u.Kind||!BehaviorSame(u.Behavior,live))return false;}
                for(int i=0;i<queueReadset.SlotCount;i++)if(queueReadset.TryReadSlot(i,out int id,out var q)&&(!sourceQueues.TryReadSlot(i,out int actual,out var live)||actual!=id||!QueueSame(q,live)))return false;
                for(int i=0;i<executionReadset.SlotCount;i++)if(executionReadset.TryReadSlot(i,out int id,out var e)&&(!sourceExecutions.TryReadSlot(i,out int actual,out var live)||actual!=id||!SpatialSame(e,live)))return false;
                for(int i=0;i<completionReadset.SlotCount;i++)if(completionReadset.TryReadSlot(i,out int id,out var c)&&(!sourceCompletions.TryReadSlot(i,out int actual,out var live)||actual!=id||!ReferenceEquals(c,live)))return false;
                return true;
            }
            internal void RejectPreparation(){if(proposalOwned){RestoreBefore();proposalOwned=false;}}
            internal void StartValidationMeasureNoThrow(){if(measure){allocatedStart=GC.GetAllocatedBytesForCurrentThread();allocationMeterArmed=true;}}
            internal void BeginNoThrow(){if(measure&&!allocationMeterArmed)allocatedStart=GC.GetAllocatedBytesForCurrentThread();Phase=TacticalCommitPhase.Committing;}
            internal void InstallNoThrow()
            {for(int i=0;i<after.Length;i++)after[i].Install();domain.tacticalQueues=nextQueues;domain.spatialExecutions=nextExecutions;domain.spatialCompletions=nextCompletions;domain.nextOrderRevision=nextRevision;domain.queueSequence=nextQueue;}
            internal void MarkCommittedNoThrow(){Phase=TacticalCommitPhase.Committed;if(measure)domain.LastTacticalCommitAllocatedBytesForQa=GC.GetAllocatedBytesForCurrentThread()-allocatedStart;}
            internal override void ReleaseOwner(){if(released)return;released=true;Release();}
            protected override void ReleaseEdges(){NavigationInputs?.Release();NavigationInputs=null;}
            internal void CommitDomainOnly()
            {
                ValidateBeforeGate();preparation.Publish(this,_=>domain.navigation.RunHostStep(()=>{StartValidationMeasureNoThrow();ValidateInGate();BeginNoThrow();InstallNoThrow();MarkCommittedNoThrow();}));preparation.ResolveCommitted();
            }
            public void Dispose(){try{preparation.Dispose();}finally{ReleaseOwner();}}
        }
    }
}

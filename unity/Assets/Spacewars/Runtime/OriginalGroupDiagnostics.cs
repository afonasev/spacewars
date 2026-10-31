using System;
using System.IO;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public static class OriginalGroupDiagnostics
    {
        private static NavigationRetentionBudget budget;
        private static Action requestStop;
        public static bool Active=>OriginalGroupTrace.Buffer!=null;
        public static bool ShouldStop=>OriginalGroupTrace.Buffer?.ShouldStop??false;
        public static int Dropped=>OriginalGroupTrace.Buffer?.Dropped??0;
        public static void StartIfRequested(){
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(Environment.GetEnvironmentVariable("SPACEWARS_ORIGINAL_GROUP_DIAGNOSTIC")=="1")Start(8192);
#endif
        }
        internal static void Start(int capacity)
        {
            if(Active)return;budget=new NavigationRetentionBudget();if(!budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope))throw new InvalidOperationException("Original diagnostic admission refused.");
            using(scope){var buffer=OriginalGroupBuffer.Create(capacity,scope.OwnedReservation());try{scope.Attach(buffer);scope.Commit(buffer,_=>{});OriginalGroupTrace.Buffer=buffer;}catch{buffer.ReleaseOwner();throw;}}
        }
        public static void Arm(Action stop=null){requestStop=stop;OriginalGroupTrace.Buffer?.Arm();}
        public static void Save(string path){if(!Active)return;using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write))OriginalGroupTrace.Buffer.Save(output);}
        public static void Stop(){var buffer=OriginalGroupTrace.Buffer;OriginalGroupTrace.Buffer=null;requestStop=null;buffer?.ReleaseOwner();if(budget!=null&&(budget.PhysicalBytes!=0||budget.HeldBytes!=0||budget.ActiveScopes!=0))throw new InvalidOperationException("Original diagnostic owner leaked.");budget=null;}
        internal static void Receipt(PlayableCommandReceipt receipt,long operation=0,long quantum=0)
        {var b=OriginalGroupTrace.Buffer;if(b==null)return;bool committed=receipt.Status==PlayableCommandStatus.Applied;b.CompleteStaged(receipt.Sequence,committed,(int)receipt.Status,operation,quantum);b.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Receipt,Sequence=receipt.Sequence,Tick=receipt.AppliedTick,Operation=operation,Quantum=quantum,Committed=committed?1:0,Status=(int)receipt.Status});}
        internal static void Accepted(PlayableCommand command,int status)
        {OriginalGroupTrace.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Accepted,Sequence=command.Sequence,Members=command.EntityIds.Count,Status=status});}
        internal static void Commit(long sequence,long operation,long quantum)=>OriginalGroupTrace.Buffer?.CompleteStaged(sequence,true,(int)PlayableCommandStatus.Applied,operation,quantum);
        internal static void Birth(NavigationArrivalWork work,long attempt){if(!Active)return;var r=work.OriginalDiagnostic(OriginalGroupEvent.Birth);r.Attempt=attempt;r.Committed=1;OriginalGroupTrace.Append(r);}
        internal static void Service(NavigationArrivalWork actual,NavigationArrivalWork proposed,long attempt,long epoch,long quantum,int grant,string outcome)
        {if(!Active)return;var r=actual.OriginalDiagnostic(OriginalGroupEvent.Service);r.Attempt=attempt;r.Epoch=epoch;r.Quantum=quantum;r.Grant=grant;r.Committed=outcome=="committed"?1:0;r.ProposedTotal=proposed?.AllocationWorkConsumed??r.Total;r.Reason=outcome=="advance-retention-blocked"?1:outcome=="scope-refused"?2:outcome=="source-owner-refused"?3:outcome=="prepared-admission-refused"?4:outcome=="exception"?5:outcome=="authority-bytes-refused"?6:outcome=="committed"?0:7;OriginalGroupTrace.Append(r);}
        internal static void Barrier(long serial,long tick){var b=OriginalGroupTrace.Buffer;if(b!=null&&b.Barrier(serial,tick))requestStop?.Invoke();}
    }
    public sealed partial class NavigationSession
    {
        internal long OriginalDiagnosticOperation=>OriginalGroupDiagnostics.Active?authorityOperationIdentity:0;
        internal long OriginalDiagnosticQuantum=>OriginalGroupDiagnostics.Active?committedHostRoot?.QuantumSerial??0:0;
        internal void OriginalDiagnosticBarrier(long tick){if(!OriginalGroupDiagnostics.Active)return;long serial;using(EnterTransport())serial=committedHostRoot?.QuantumSerial??tick;OriginalGroupDiagnostics.Barrier(serial,tick);}
        private OriginalGroupRecord OriginalRuntimeRecord(NavigationRequest request,OriginalGroupEvent kind,long sequence)
            =>new OriginalGroupRecord{Kind=kind,Sequence=sequence,Entity=request.Entity,Request=request.Request,Group=request.MemberIdentity?.GroupId??0,Operation=authorityOperationIdentity,Quantum=committedHostRoot?.QuantumSerial??0,Geometry=request.BaseGeometry.Revision,Profile=request.ProfileRevision,PendingCount=pending.Count,RegisteredCount=RegisteredRouteCount,InstalledCount=installedExecutions.Count};
        private bool TryPublishOwnedArrivalObserved(ArrivalAttempt attempt)
        {
            if(!OriginalGroupDiagnostics.Active)return TryPublishOwnedArrival(attempt);
            var r=attempt.Work.OriginalDiagnostic(OriginalGroupEvent.Publication);r.Attempt=attempt.Id;r.Quantum=committedHostRoot?.QuantumSerial??0;
            try{return TryPublishOwnedArrival(attempt);}finally{r.Committed=attempt.Published?1:0;r.Operation=authorityOperationIdentity;r.PendingCount=pending.Count;r.RegisteredCount=RegisteredRouteCount;r.InstalledCount=installedExecutions.Count;OriginalGroupTrace.Append(r);}
        }
    }
    internal sealed partial class PlayableDomain
    {
        private void StageOriginalQueue(PlayableCommand command,int id,TacticalQueue q)
        {var b=OriginalGroupTrace.Buffer;if(b==null)return;var u=units[id];var e=q?.Pending??q?.Active;b.Stage(new OriginalGroupRecord{Kind=e==null&&u.CurrentOrder==null?OriginalGroupEvent.Terminal:OriginalGroupEvent.Recipient,Sequence=command.Sequence,Entity=id,Tick=Tick,Group=e?.Issuance.GroupId??0,Request=e?.AttemptRequest??0,PendingSequence=q?.Pending?.Issuance.Command.Sequence??0,ActiveSequence=q?.Active?.Issuance.Command.Sequence??0,CurrentSequence=u.CurrentOrder?.CommandSequence??0,CurrentPhase=q?.Pending!=null?1:q?.Active!=null?2:0});}
        private void TraceOriginalQueue(NavigationRequest request,OriginalGroupEvent kind,int status=0)
        {
            if(!OriginalGroupDiagnostics.Active||!tacticalQueues.TryGetValue(request.Entity,out var q))return;
            long sequence=q.Pending?.Issuance.Command.Sequence??q.Active?.Issuance.Command.Sequence??0;
            var r=navigation.OriginalQueueRecord(request,kind,sequence);r.Tick=Tick;r.Committed=1;r.Status=status;r.PendingSequence=q.Pending?.Issuance.Command.Sequence??0;r.ActiveSequence=q.Active?.Issuance.Command.Sequence??0;r.CurrentSequence=units[request.Entity].CurrentOrder?.CommandSequence??0;OriginalGroupTrace.Append(r);
        }
    }
    public sealed partial class NavigationSession
    {
        internal OriginalGroupRecord OriginalQueueRecord(NavigationRequest request,OriginalGroupEvent kind,long sequence)=>OriginalRuntimeRecord(request,kind,sequence);
    }
}

#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Opt-in QA receipt lane. Existing runtime gate owns all writes; no I/O here.
    public sealed class QaPerformanceReceipts
    {
        internal const int ExpectedCapacity=8,EventCapacity=64,ReceiptMessageCapacity=512;
        public readonly struct Row
        {
            public readonly string Owner,Phase,AdmissionDiagnostic,ReceiptMessage;public readonly long Generation,Sequence,Tick;public readonly PlayableCommandStatus Status;public readonly double MonotonicSeconds,LatencyMilliseconds;
            internal Row(string owner,long generation,long sequence,long tick,PlayableCommandStatus status,string phase,double latency,string admissionDiagnostic=null,string receiptMessage=null){ReceiptMessage=receiptMessage!=null&&receiptMessage.Length>ReceiptMessageCapacity?receiptMessage.Substring(0,ReceiptMessageCapacity):receiptMessage;AdmissionDiagnostic=admissionDiagnostic;Owner=owner;Generation=generation;Sequence=sequence;Tick=tick;Status=status;Phase=phase;LatencyMilliseconds=latency;MonotonicSeconds=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;}
        }
        private readonly PlayableCommand[] commands=new PlayableCommand[ExpectedCapacity];
        private readonly string[] owners=new string[ExpectedCapacity];private readonly long[] generations=new long[ExpectedCapacity],sequences=new long[ExpectedCapacity];
        private readonly Row[] rows=new Row[EventCapacity];private int expected,count,dropped;
        public int Expected=>expected;public int Dropped=>dropped;
        private int Find(string owner,long sequence){for(int i=0;i<expected;i++)if(owners[i]==owner&&sequences[i]==sequence)return i;return -1;}
        internal void Register(PlayableCommand command)
        {
            if(Find(command.PlayerId,command.Sequence)>=0)return;
            for(int i=0;i<expected;i++)if(owners[i]==command.PlayerId){dropped++;return;}
            if(expected==ExpectedCapacity){dropped++;return;}commands[expected]=command;owners[expected]=command.PlayerId;generations[expected]=command.Generation;sequences[expected++]=command.Sequence;
        }
        private void Add(Row row){if(count==rows.Length){dropped++;return;}rows[count++]=row;}
        internal void Submitted(PlayableCommand command,PlayableCommandStatus status,long tick){int i=Find(command.PlayerId,command.Sequence);if(i>=0)Add(new Row(owners[i],generations[i],sequences[i],tick,status,"submission",double.NaN));}
        internal void Receipt(PlayableCommandReceipt receipt,string admissionDiagnostic=null){int i=Find(receipt.OwnerId,receipt.Sequence);if(i>=0)Add(new Row(owners[i],generations[i],sequences[i],receipt.AppliedTick,receipt.Status,"receipt",receipt.LatencyMilliseconds,admissionDiagnostic,receipt.Message));}
        public static string CsvHeader=>"owner,generation,sequence,tick,phase,status,monotonic_seconds,latency_ms,admission_diagnostic,receipt_message";
        // Formatting is called only by the existing exporter after producers stop.
        public static string CsvRow(Row row)=>string.Format(CultureInfo.InvariantCulture,"{0},{1},{2},{3},{4},{5},{6:R},{7:R}",row.Owner,row.Generation,row.Sequence,row.Tick,row.Phase,row.Status,row.MonotonicSeconds,row.LatencyMilliseconds)+",\""+(row.AdmissionDiagnostic??"").Replace("\"","\"\"")+"\",\""+(row.ReceiptMessage??"").Replace("\"","\"\"")+"\"";
        internal PlayableCommand[] CopyCommands(){var copy=new PlayableCommand[expected];Array.Copy(commands,copy,expected);return copy;}
        internal Row[] Copy(){var copy=new Row[count];Array.Copy(rows,copy,count);return copy;}
    }
    // Append-only scalar records from completed navigation steps. One authority
    // producer, no I/O, no detailed movement observer, no persisted state.
    public sealed class QaPerformanceNavigationSteps
    {
        internal const int Capacity=8192;
        public struct Row
        {
            public long Tick,Generation,Stamp,HostSerial,HostProgress,HostBytes,RetainedBytes,HeldBytes,MovementRefusals;
            public double Milliseconds;
            public int Path,MovementReason,TrafficStage,BatchStage,BatchSource,Applied,ResultPublications,MovementPublications,
                Answers,BarrierAnswers,Pending,Allocating,HostStage,AllocationThread,Scopes,MovementRefusal;
        }
        private readonly Row[] rows=new Row[Capacity];private int count,dropped;
        internal void Add(Row row){int at=count;if(at==rows.Length){System.Threading.Interlocked.Increment(ref dropped);return;}rows[at]=row;System.Threading.Volatile.Write(ref count,at+1);}
        public Row[] Copy(out int lost){int length=System.Threading.Volatile.Read(ref count);var copy=new Row[length];Array.Copy(rows,copy,length);lost=System.Threading.Volatile.Read(ref dropped);return copy;}
        public void WriteCsv(string path)
        {
            var copy=Copy(out int lost);using(var w=new StreamWriter(path)){
                w.WriteLine("tick,generation,stamp,navigation_ms,step_path,movement_reason,traffic_stage,last_batch_stage,last_batch_source,applied_results,result_publications,movement_publications,answers,barrier_answers,pending,allocating,host_stage,allocation_thread,host_serial,host_progress,host_bytes,retained_bytes,held_bytes,scopes,last_movement_refusal,movement_refusals");
                foreach(var r in copy)w.WriteLine(string.Format(CultureInfo.InvariantCulture,"{0},{1},{2},{3:R},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25}",r.Tick,r.Generation,r.Stamp,r.Milliseconds,r.Path,r.MovementReason,r.TrafficStage,r.BatchStage,r.BatchSource,r.Applied,r.ResultPublications,r.MovementPublications,r.Answers,r.BarrierAnswers,r.Pending,r.Allocating,r.HostStage,r.AllocationThread,r.HostSerial,r.HostProgress,r.HostBytes,r.RetainedBytes,r.HeldBytes,r.Scopes,r.MovementRefusal,r.MovementRefusals));
                w.WriteLine("# rows="+copy.Length+",dropped="+lost+",capacity="+Capacity+",frequency="+Stopwatch.Frequency);
            }
        }
    }
    public struct QaPerformanceQueueRow
    {
        public int Actor,Owner,Navigation,NavMemberPhase,AdmissionRequests;
        public long ExpectedSequence,PendingSequence,ActiveSequence,CurrentSequence,PendingGroup,ActiveGroup,NavGroup,AttemptRequest,AdmissionPublication,AuthorityOperation,CompletionSequence,CompletionGroup,CompletionTick;
        public bool CompletionSpatial,ActorPresent,PendingArrived,PendingFailed,ActiveArrived,ActiveFailed;
        public static QaPerformanceQueueRow Unknown=>new QaPerformanceQueueRow{ExpectedSequence=-1,PendingSequence=-1,ActiveSequence=-1,CurrentSequence=-1,NavMemberPhase=-1,AdmissionRequests=-1,Navigation=-1};
    }
    public sealed class QaPerformanceQueueFrame
    {
        private readonly QaPerformanceQueueRow[] rows;
        public long Generation{get;}public long Tick{get;}public int Count=>rows.Length;public int Dropped{get;}public double CaptureMilliseconds{get;internal set;}
        internal QaPerformanceQueueFrame(long generation,long tick,QaPerformanceQueueRow[] rows,int dropped){Generation=generation;Tick=tick;this.rows=rows;Dropped=dropped;Array.Sort(rows,(a,b)=>a.Actor.CompareTo(b.Actor));}
        public void WriteCsv(string path){
            using(var w=new StreamWriter(path)){
                w.WriteLine("generation,tick,capture_ms,actor,owner,actor_present,expected_sequence,pending_sequence,active_sequence,current_sequence,pending_group,active_group,nav_group,nav_member_phase,navigation,actual_admission_requests,admission_publication,authority_operation,attempt_request,pending_arrived,pending_failed,active_arrived,active_failed,completion_sequence,completion_group,completion_tick,completion_spatial");
                foreach(var r in rows)w.WriteLine(string.Format(CultureInfo.InvariantCulture,"{0},{1},{2:R},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26}",Generation,Tick,CaptureMilliseconds,r.Actor,r.Owner,r.ActorPresent?1:0,r.ExpectedSequence,r.PendingSequence,r.ActiveSequence,r.CurrentSequence,r.PendingGroup,r.ActiveGroup,r.NavGroup,r.NavMemberPhase,r.Navigation,r.AdmissionRequests,r.AdmissionPublication,r.AuthorityOperation,r.AttemptRequest,r.PendingArrived?1:0,r.PendingFailed?1:0,r.ActiveArrived?1:0,r.ActiveFailed?1:0,r.CompletionSequence,r.CompletionGroup,r.CompletionTick,r.CompletionSpatial?1:0));
                w.WriteLine("# rows="+rows.Length+",dropped="+Dropped);
            }
        }
        public bool TryActor(int actor,out QaPerformanceQueueRow row){int lo=0,hi=rows.Length-1;while(lo<=hi){int m=(lo+hi)/2;if(rows[m].Actor==actor){row=rows[m];return true;}if(rows[m].Actor<actor)lo=m+1;else hi=m-1;}row=QaPerformanceQueueRow.Unknown;return false;}
    }
    internal sealed partial class PlayableDomain
    {
        private QaPerformanceNavigationSteps qaNavigationSteps;
        internal QaPerformanceNavigationSteps NavigationStepsForQa=>System.Threading.Volatile.Read(ref qaNavigationSteps);
        internal void EnableNavigationStepsForQa(){if(NavigationStepsForQa==null)System.Threading.Interlocked.CompareExchange(ref qaNavigationSteps,new QaPerformanceNavigationSteps(),null);}
        private void RecordNavigationStepForQa(long began)
        {
            var recorder=NavigationStepsForQa;if(recorder==null)return;
            double elapsed=(Stopwatch.GetTimestamp()-began)*1000d/Stopwatch.Frequency;
            recorder.Add(navigation.ObserveNavigationStepForQa(Tick,elapsed));
        }
        internal QaPerformanceQueueFrame CapturePerformanceQueues(PlayableCommand[] commands)
        {
            const int maximum=400;int total=0;foreach(var command in commands)total=checked(total+command.EntityIds.Count);
            var rows=new QaPerformanceQueueRow[Math.Min(maximum,total)];int count=0;
            foreach(var command in commands)foreach(int id in command.EntityIds){
                if(count==rows.Length)break;var row=QaPerformanceQueueRow.Unknown;row.Actor=id;row.Owner=(int)OwnerFor(command.PlayerId);row.ExpectedSequence=command.Sequence;
                if(units.TryGetValue(id,out var unit)){
                    row.ActorPresent=true;row.Owner=(int)unit.Owner;row.CurrentSequence=unit.CurrentOrder?.CommandSequence??0;
                    if(TryActor(id,out var actor))row.Navigation=(int)actor.Outcome;
                    if(tacticalQueues.TryGetValue(id,out var q)){
                        row.PendingSequence=q.Pending?.Issuance.Command.Sequence??0;row.ActiveSequence=q.Active?.Issuance.Command.Sequence??0;
                        row.PendingGroup=q.Pending?.Issuance.GroupId??0;row.ActiveGroup=q.Active?.Issuance.GroupId??0;row.AttemptRequest=q.Pending?.AttemptRequest??q.Active?.AttemptRequest??0;
                        row.PendingArrived=q.Pending?.Arrived??false;row.PendingFailed=q.Pending?.Failed??false;row.ActiveArrived=q.Active?.Arrived??false;row.ActiveFailed=q.Active?.Failed??false;
                        row.CompletionSequence=q.Completion?.Order.Sequence??0;row.CompletionGroup=q.Completion?.GroupId??0;row.CompletionTick=q.Completion?.CompletionTick??0;row.CompletionSpatial=q.Completion?.SpatialReceipt!=null;
                    }else{row.PendingSequence=row.ActiveSequence=0;}
                }
                rows[count++]=row;
            }
            navigation.FillPerformanceQueueNavigation(rows);
            return new QaPerformanceQueueFrame(navigation.Generation,Tick,rows,total-rows.Length);
        }
    }
    public sealed partial class NavigationSession
    {
        internal QaPerformanceNavigationSteps.Row ObserveNavigationStepForQa(long tick,double milliseconds)
        {
            using(EnterTransport())return new QaPerformanceNavigationSteps.Row{
                Tick=tick,Generation=Generation,Stamp=Stopwatch.GetTimestamp(),Milliseconds=milliseconds,
                Path=LastNavigationStepPath,MovementReason=LastOwnedMovementNullReason,MovementRefusal=LastOwnedMovementRefusal,MovementRefusals=OwnedMovementRefusals,TrafficStage=LastTrafficDispatchStage,
                BatchStage=LastResultBatchStage,BatchSource=LastResultBatchSourceReason,Applied=AppliedResults,
                ResultPublications=OwnedResultBatchPublications,MovementPublications=OwnedMovementPublications,
                Answers=Answers.Count,BarrierAnswers=barrierAnswers.Count,Pending=pending.Count,Allocating=allocating.Count,
                HostStage=committedHostRoot==null?-1:(int)committedHostRoot.Frontier.Stage,AllocationThread=hostAllocationThread,
                HostSerial=committedHostRoot?.QuantumSerial??-1,HostProgress=committedHostRoot?.Progress??-1,
                HostBytes=hostRetainedBytes,RetainedBytes=physicalHostBudget.RetainedBytes,HeldBytes=physicalHostBudget.HeldBytes,Scopes=physicalHostBudget.ActiveScopes};
        }
        internal void FillPerformanceQueueNavigation(QaPerformanceQueueRow[] rows)
        {
            using(EnterTransport()){
                int requests=admissionOwner?.Requests.Count??-1;
                for(int i=0;i<rows.Length;i++){
                    var row=rows[i];row.AdmissionRequests=requests;row.AdmissionPublication=admissionPublicationIdentity;row.AuthorityOperation=authorityOperationIdentity;
                    if(memberGroups.TryGetValue(row.Actor,out var groupId)&&groups.TryGetValue(groupId,out var group)){
                        row.NavGroup=groupId;for(int m=0;m<group.Members.Length;m++)if(group.Members[m].Entity==row.Actor){row.NavMemberPhase=(int)group.Members[m].Phase;break;}
                    }
                    rows[i]=row;
                }
            }
        }
    }
    public sealed partial class PlayableRuntime
    {
        private static readonly bool QaReadoutsRequested=Array.IndexOf(Environment.GetCommandLineArgs(),"-q10Readouts")>=0;
        public bool PerformanceReadoutsEnabled=>QaReadoutsRequested;
        private QaPerformanceReceipts qaPerformanceReceipts;
        private QaPerformanceQueueFrame qaPerformanceQueues;private double qaNextQueueReadout;
        public QaPerformanceQueueFrame PerformanceQueueFrame=>System.Threading.Volatile.Read(ref qaPerformanceQueues);
        private void PublishPerformanceQueues()
        {
            double now=Timestamp();PlayableCommand[] commands;
            lock(gate){if(qaPerformanceReceipts==null||now<qaNextQueueReadout)return;commands=qaPerformanceReceipts.CopyCommands();qaNextQueueReadout=now+.2;}
            long began=Stopwatch.GetTimestamp();var frame=domain.CapturePerformanceQueues(commands);frame.CaptureMilliseconds=(Stopwatch.GetTimestamp()-began)*1000d/Stopwatch.Frequency;
            System.Threading.Volatile.Write(ref qaPerformanceQueues,frame);
        }
        public bool WritePerformanceNavigationSteps(string path){var steps=domain.NavigationStepsForQa;if(steps==null)return false;steps.WriteCsv(path);return true;}
        public QaPerformanceReceipts.Row[] CopyPerformanceReceipts(out int expected,out int dropped)
        {lock(gate){expected=qaPerformanceReceipts?.Expected??0;dropped=qaPerformanceReceipts?.Dropped??0;return qaPerformanceReceipts?.Copy()??Array.Empty<QaPerformanceReceipts.Row>();}}
    }
}
#endif

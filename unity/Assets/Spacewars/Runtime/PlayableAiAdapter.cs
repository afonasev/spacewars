using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Diagnostic delivery adapter. It deliberately has no policy, hidden-domain access, or navigation authority.
    public sealed class PlayableAiAdapter
    {
        private sealed class Pending { public PlayableAiObservation Observation; public PlayableAiAction Action; public long DueTick; }
        private readonly PlayableRuntime runtime;
        private readonly List<Pending> pending=new List<Pending>();
        private readonly List<PlayableAiTraceRecord> trace=new List<PlayableAiTraceRecord>();
        private readonly Dictionary<long,Pending> submitted=new Dictionary<long,Pending>();
        private long nextSequence;
        public PlayableAiAdapter(PlayableRuntime runtime,long firstCommandSequence=1)
        {this.runtime=runtime??throw new ArgumentNullException(nameof(runtime));if(firstCommandSequence<1)throw new ArgumentOutOfRangeException(nameof(firstCommandSequence));nextSequence=firstCommandSequence;}
        public IReadOnlyList<PlayableAiTraceRecord> Trace=>trace.AsReadOnly();
        public PlayableAiObservation Observe()=>PlayableAiObservation.From(runtime.Latest);
        public PlayableAiDeliveryStatus Schedule(PlayableAiObservation observation,PlayableAiAction action,int delayTicks)
        {
            if(observation==null||action==null||delayTicks<0)return Record(observation,action,0,0,PlayableAiDeliveryStatus.InvalidAction,null,"Invalid adapter decision.");
            if(action.PlayerId!=PlayableDomain.PlayerId)return Record(observation,action,0,0,PlayableAiDeliveryStatus.InvalidOwner,null,"Native adapter accepts only the local owner.");
            if(action.ProfileId!=observation.ProfileId||action.ProfileRevision!=observation.ProfileRevision||action.Generation!=observation.Generation||action.SnapshotSequence!=observation.SnapshotSequence)
                return Record(observation,action,0,0,PlayableAiDeliveryStatus.Stale,null,"Action does not bind to its observation.");
            if(!Supported(action.Kind))return Record(observation,action,0,0,PlayableAiDeliveryStatus.InvalidAction,null,"Unsupported schema9 action.");
            var item=new Pending{Observation=observation,Action=action,DueTick=observation.Tick+delayTicks};pending.Add(item);
            return Record(observation,action,item.DueTick,0,PlayableAiDeliveryStatus.Scheduled,null,"Scheduled.");
        }
        public PlayableAiDeliveryStatus Cancel(long actionId)
        {
            var item=pending.FirstOrDefault(x=>x.Action.ActionId==actionId);
            if(item==null)return Record(null,new PlayableAiAction(actionId,"player-1","",0,0,0,PlayableCommandKind.Move),0,0,PlayableAiDeliveryStatus.InvalidAction,null,"No pending action.");
            pending.Remove(item);return Record(item.Observation,item.Action,item.DueTick,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Cancelled before delivery.");
        }
        public PlayableAiDeliveryStatus RejectDiagnosticIdentity(PlayableAiObservation observation,PlayableAiAction action)
            =>Record(observation,action,0,0,PlayableAiDeliveryStatus.Stale,null,"Evaluator provenance does not match the frozen diagnostic identity.");
        public void Pump()
        {
            var latest=runtime.Latest;
            foreach(var item in pending.Where(x=>x.DueTick<=latest.Tick).OrderBy(x=>x.DueTick).ThenBy(x=>x.Action.ActionId).ToArray())
            {
                pending.Remove(item);
                if(latest.Generation!=item.Action.Generation||latest.ProfileId!=item.Action.ProfileId||latest.ProfileRevision!=item.Action.ProfileRevision||latest.Sequence<item.Action.SnapshotSequence)
                {Record(item.Observation,item.Action,item.DueTick,0,PlayableAiDeliveryStatus.Stale,null,"Observation binding is stale.");continue;}
                if(latest.Paused)
                {Record(item.Observation,item.Action,item.DueTick,0,PlayableAiDeliveryStatus.Rejected,PlayableCommandStatus.Rejected,"Runtime paused.");continue;}
                if(runtime.IsStopped)
                {Record(item.Observation,item.Action,item.DueTick,0,PlayableAiDeliveryStatus.Stopped,PlayableCommandStatus.Stopped,"Runtime stopped.");continue;}
                long sequence=nextSequence++;var command=Command(item.Action,sequence);var admission=runtime.TrySubmitAi(command);
                if(!admission.Accepted){Record(item.Observation,item.Action,item.DueTick,sequence,Map(admission.Status),admission.Status,"Runtime admission rejected.");continue;}
                submitted.Add(sequence,item);Record(item.Observation,item.Action,item.DueTick,sequence,PlayableAiDeliveryStatus.Accepted,admission.Status,"Admitted.");
            }
            foreach(var receipt in runtime.ReceiptsAfter(0).OrderBy(x=>x.Sequence))if(submitted.TryGetValue(receipt.Sequence,out var item))
            {
                submitted.Remove(receipt.Sequence);Record(item.Observation,item.Action,item.DueTick,receipt.Sequence,receipt.AppliedTick,Map(receipt.Status),receipt.Status,receipt.Message);
            }
        }
        private PlayableAiDeliveryStatus Record(PlayableAiObservation observation,PlayableAiAction action,long due,long sequence,PlayableAiDeliveryStatus status,PlayableCommandStatus? runtimeStatus,string message)=>Record(observation,action,due,sequence,0,status,runtimeStatus,message);
        private PlayableAiDeliveryStatus Record(PlayableAiObservation observation,PlayableAiAction action,long due,long sequence,long applicationTick,PlayableAiDeliveryStatus status,PlayableCommandStatus? runtimeStatus,string message)
        {trace.Add(new PlayableAiTraceRecord(observation?.Identity??"",action?.ActionId??0,due,sequence,applicationTick,status,runtimeStatus,message,observation?.OwnerId,action?.SourceIdentity));return status;}
        private static PlayableAiDeliveryStatus Map(PlayableCommandStatus status)
        {return status==PlayableCommandStatus.Accepted?PlayableAiDeliveryStatus.Accepted:status==PlayableCommandStatus.Applied?PlayableAiDeliveryStatus.Applied:status==PlayableCommandStatus.Cancelled?PlayableAiDeliveryStatus.Cancelled:status==PlayableCommandStatus.Stopped?PlayableAiDeliveryStatus.Stopped:status==PlayableCommandStatus.StaleGeneration?PlayableAiDeliveryStatus.Stale:PlayableAiDeliveryStatus.Rejected;}
        private static bool Supported(PlayableCommandKind kind)=>Enum.IsDefined(typeof(PlayableCommandKind),kind)&&kind!=PlayableCommandKind.Restart;
        private static PlayableCommand Command(PlayableAiAction a,long sequence)=>new PlayableCommand(a.Generation,sequence,a.PlayerId,a.Kind,a.CopyEntityIds(),a.Target,siteId:a.SiteId,slotId:a.SlotId,buildingKind:a.BuildingKind,parentId:a.ParentId,targetId:a.TargetId,productionOrderId:a.ProductionOrderId,unitKind:a.UnitKind,researchKind:a.ResearchKind,origin:PlayableOrderOrigin.Ai,source:a.SourceIdentity??"diagnostic:PlayableAiAdapter:unbound-source",jobId:a.ActionId,actionId:a.ActionId);
    }

    // Caller supplies scenario/source identities and declarative actions; no policy is embedded here.
    public sealed class PlayableAiDiagnosticRun
    {
        private readonly PlayableAiAdapter adapter;
        public PlayableAiDiagnosticRun(PlayableRuntime runtime,string scenarioId,string sourceIdentity,long firstCommandSequence=1):this(runtime,scenarioId,sourceIdentity,"adapter-u6-fixtures-v1",firstCommandSequence){}
        public PlayableAiDiagnosticRun(PlayableRuntime runtime,string scenarioId,string sourceIdentity,string fixtureBankIdentity,long firstCommandSequence=1)
        {adapter=new PlayableAiAdapter(runtime,firstCommandSequence);Identity=new PlayableAiDiagnosticIdentity(scenarioId,sourceIdentity,fixtureBankIdentity,adapter.Observe());OpeningCompositionAuthority=runtime.OpeningCompositionAuthority;OpeningComposition=runtime.OpeningComposition;}
        public PlayableAiDiagnosticIdentity Identity{get;}
        public PlayableAiOpeningCompositionAuthority OpeningCompositionAuthority{get;}
        public PlayableAiOpeningCompositionState OpeningComposition{get;}
        public IReadOnlyList<PlayableAiTraceRecord> Records=>adapter.Trace;
        public PlayableAiObservation Observe()=>adapter.Observe();
        public PlayableAiDeliveryStatus Schedule(PlayableAiObservation observation,PlayableAiAction action,int delayTicks)=>!Identity.Matches(observation)?adapter.RejectDiagnosticIdentity(observation,action):adapter.Schedule(observation,action,delayTicks);
        public PlayableAiDeliveryStatus Schedule(PlayableAiAction action,int delayTicks)=>Schedule(Observe(),action,delayTicks);
        // Cancels an older assault before admitting a center-defense order; caller pumps receipts normally.
        public PlayableAiAction ScheduleTactical(PlayableAiMissionDefensePolicy policy,PlayableAiOpeningCompositionState opening,int delayTicks=0)
        {
            if(policy==null)throw new ArgumentNullException(nameof(policy));
            var observation=Observe();var action=policy.TryPlan(observation,opening);
            if(policy.PreemptedActionId!=0)Cancel(policy.PreemptedActionId);
            if(action!=null)Schedule(observation,action,delayTicks);
            return action;
        }
        public PlayableAiAction ScheduleArtillery(PlayableAiArtillerySupportPolicy policy,PlayableAiOpeningCompositionState opening,int delayTicks=0)
        {
            if(policy==null)throw new ArgumentNullException(nameof(policy));
            var observation=Observe();var action=policy.TryPlan(observation,opening);
            if(action!=null)Schedule(observation,action,delayTicks);
            return action;
        }
        public PlayableAiDeliveryStatus Cancel(long actionId)=>adapter.Cancel(actionId);
        public void Pump()=>adapter.Pump();
        public PlayableAiDiagnosticTimeline CaptureTimeline()=>new PlayableAiDiagnosticTimeline(Identity,Records);
    }
}

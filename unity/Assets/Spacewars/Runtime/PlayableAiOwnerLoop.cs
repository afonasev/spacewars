using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Immutable publication from the match authority. This is diagnostic evidence, not a save format.
    [Serializable]
    public sealed class PlayableAiOwnerCheckpoint
    {
        private readonly PlayableAiTraceRecord[] records;
        public PlayableAiOwnerCheckpoint(string ownerId,string sourceIdentity,string profileId,int profileRevision,int seed,long generation,long decisionTick,long lastScoutTick,long pendingActionId,long pendingDueTick,long lastCommandSequence,
            string lastPolicy,PlayableCommandKind? lastActionKind,PlayableAiOpeningCompositionState opening,PlayableAiCombatMission mission,PlayableAiMidgameCheckpoint strategy,PlayableAiArtilleryState artillery,IEnumerable<PlayableAiTraceRecord> records)
        {
            OwnerId=ownerId;SourceIdentity=sourceIdentity;ProfileId=profileId;ProfileRevision=profileRevision;Seed=seed;
            Generation=generation;DecisionTick=decisionTick;LastScoutTick=lastScoutTick;
            PendingActionId=pendingActionId;PendingDueTick=pendingDueTick;LastCommandSequence=lastCommandSequence;
            LastPolicy=lastPolicy;LastActionKind=lastActionKind;Opening=opening?.Restore();Mission=mission?.Copy();Strategy=strategy;Artillery=artillery;
            this.records=records.ToArray();
        }
        public string OwnerId{get;} public string SourceIdentity{get;} public string ProfileId{get;} public int ProfileRevision{get;} public int Seed{get;} public long Generation{get;}
        public long DecisionTick{get;} public long LastScoutTick{get;} public long PendingActionId{get;} public long PendingDueTick{get;} public long LastCommandSequence{get;}
        public string LastPolicy{get;} public PlayableCommandKind? LastActionKind{get;}
        public PlayableAiOpeningCompositionState Opening{get;} public PlayableAiCombatMission Mission{get;}
        public PlayableAiMidgameCheckpoint Strategy{get;} public PlayableAiArtilleryState Artillery{get;}
        public IReadOnlyList<PlayableAiTraceRecord> Records=>Array.AsReadOnly(records);
    }

    // Called only by PlayableRuntime's authority thread. All policy input is a fog-safe projection.
    internal sealed class PlayableAiOwnerLoop
    {
        internal string OwnerId{get;}
        private const long DecisionIntervalTicks=45; // frozen release:8 fighter decisionIntervalTicks
        private const long ObservationDelayTicks=30; // frozen release:8 fighter observationDelayTicks
        private const long RescoutTicks=35*30; // frozen release:8 economy.techRescoutSec
        private sealed class Pending
        {
            public PlayableAiObservation Observation;
            public PlayableAiAction Action;
            public long DueTick,HumanSequence,Ordinal;
            public Action<PlayableAiTraceRecord> Receipt;
            public bool Scout;
            public string PolicyName;
        }
        private PlayableProfile profile;
        internal void Rebind(PlayableProfile next){if(pending!=null)Cancel(PlayableAiDeliveryStatus.Cancelled,"Balance revision changed.");profile=next;production.Rebind(next);mission.Rebind(next);midgame.Rebind(next);artillery.Rebind(next);Checkpoint=Capture();}
        private readonly PlayableAiOpeningCompositionState opening;
        private readonly PlayableAiEconomicLivenessPolicy economy;
        private readonly PlayableAiProductionLivenessPolicy production;
        private readonly PlayableAiResearchLivenessPolicy research;
        private readonly PlayableAiResearchReadinessTracker readiness=new PlayableAiResearchReadinessTracker();
        private readonly PlayableAiScoutLivenessPolicy scout;
        private readonly PlayableAiMissionDefensePolicy mission;
        private readonly PlayableAiMidgameStrategyPolicy midgame;
        private readonly PlayableAiArtillerySupportPolicy artillery;
        private readonly List<PlayableAiTraceRecord> records=new List<PlayableAiTraceRecord>();
        private Pending pending;
        private long lastDecisionTick,lastScoutTick=-1,lastCommandSequence,ordinal,authorityTick;
        private readonly Action<PlayableAiEvaluationEvent> audit;
        private string lastPolicy;
        private PlayableCommandKind? lastActionKind;
        // This range is authority-owned and distinct from external input sequence values.
        private long nextCommandSequence;
        internal PlayableAiOwnerLoop(PlayableProfile profile,PlayableAiOpeningCompositionState opening,long generation,Action<PlayableAiEvaluationEvent> audit=null)
        {
            this.audit=audit;
            this.profile=profile??throw new ArgumentNullException(nameof(profile));
            this.opening=opening??throw new ArgumentNullException(nameof(opening));
            if(opening.OwnerId!=PlayableDomain.PlayerId&&opening.OwnerId!="enemy-1"||opening.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity)throw new ArgumentException("Owner/source mismatch.",nameof(opening));
            OwnerId=opening.OwnerId;
            // Disjoint authority-only sequence ranges keep both owners independent of external input.
            nextCommandSequence=OwnerId==PlayableDomain.PlayerId?1L<<60:1L<<61;
            Generation=generation;mission=new PlayableAiMissionDefensePolicy(profile,ownerId:OwnerId);midgame=new PlayableAiMidgameStrategyPolicy(profile,OwnerId);artillery=new PlayableAiArtillerySupportPolicy(profile,ownerId:OwnerId);
            economy=new PlayableAiEconomicLivenessPolicy(ownerId:OwnerId);research=new PlayableAiResearchLivenessPolicy(ownerId:OwnerId);scout=new PlayableAiScoutLivenessPolicy(ownerId:OwnerId);
            production=new PlayableAiProductionLivenessPolicy(profile:profile,ownerId:OwnerId);
            Checkpoint=Capture();
        }
        internal long Generation{get;}
        internal PlayableAiOwnerCheckpoint Checkpoint{get;private set;}
        internal void Deliver(PlayableDomain domain,long humanSequence,bool paused)
        {
            authorityTick=domain.Tick;
            if(pending==null)return;
            if(paused){Cancel(PlayableAiDeliveryStatus.Cancelled,"Runtime paused.");return;}
            if(domain.Outcome!=PlayableMatchOutcome.Playing){Cancel(PlayableAiDeliveryStatus.Stopped,"Match finished.");return;}
            if(humanSequence!=pending.HumanSequence){Cancel(PlayableAiDeliveryStatus.Cancelled,"Newer human command has priority.");return;}
            if(domain.Tick<pending.DueTick)return;
            var item=pending;pending=null;
            var action=item.Action;
            if(action.Generation!=Generation||action.PlayerId!=OwnerId||action.Seed!=opening.MatchSeed||action.SourceIdentity!=opening.SourceIdentity||action.ProfileId!=profile.ProfileId||action.ProfileRevision!=profile.Revision||action.SnapshotSequence!=item.Observation.SnapshotSequence)
            {
                Finish(item,0,0,PlayableAiDeliveryStatus.Stale,PlayableCommandStatus.StaleGeneration,"Observation binding changed.");return;
            }
            long sequence=nextCommandSequence++;lastCommandSequence=sequence;
            var command=new PlayableCommand(action.Generation,sequence,action.PlayerId,action.Kind,action.CopyEntityIds(),action.Target,
                siteId:action.SiteId,slotId:action.SlotId,buildingKind:action.BuildingKind,parentId:action.ParentId,targetId:action.TargetId,
                productionOrderId:action.ProductionOrderId,unitKind:action.UnitKind,researchKind:action.ResearchKind,origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:action.ActionId,actionId:action.ActionId);
            PlayableCommandStatus status;string message;
            try {status=domain.Apply(command,out message);}
            catch(Exception ex){status=PlayableCommandStatus.Rejected;message=ex.Message;}
            if(item.Scout&&status==PlayableCommandStatus.Applied)lastScoutTick=domain.Tick;
            Finish(item,sequence,domain.Tick,status==PlayableCommandStatus.Applied?PlayableAiDeliveryStatus.Applied:PlayableAiDeliveryStatus.Rejected,status,message);
        }
        internal void Review(PlayableSnapshot snapshot,long humanSequence)
        {
            authorityTick=snapshot.Tick;
            if(snapshot.Generation!=Generation||snapshot.Status==RuntimeStatus.Stopped||snapshot.Status==RuntimeStatus.Failed||snapshot.Outcome!=PlayableMatchOutcome.Playing){if(pending!=null)Cancel(PlayableAiDeliveryStatus.Stopped,"Match ended.");return;}
            if(snapshot.Paused||pending!=null||snapshot.Tick-lastDecisionTick<DecisionIntervalTicks)return;
            lastDecisionTick=snapshot.Tick;
            var observation=PlayableAiObservation.From(snapshot);
            if(observation.OwnerId!=OwnerId)throw new InvalidOperationException("Owner loop received a foreign projection.");
            var strategy=midgame.Review(observation,opening,mission.Mission);
            PlayableAiAction action=null;Action<PlayableAiTraceRecord> receipt=null;bool isScout=false;string policyName=null;
            void Choose(string name,PlayableAiAction candidate,Action<PlayableAiTraceRecord> observer)
            {if(action==null&&candidate!=null){action=candidate;receipt=observer;policyName=name;}}
            Choose("mission-defense",mission.TryPlan(observation,opening),mission.ObserveReceipt);
            if(action==null)Choose("artillery",artillery.TryPlan(observation,opening,strategy),artillery.ObserveReceipt);
            if(action==null)Choose("economy",economy.TryPlan(observation),economy.ObserveReceipt);
            if(action==null&&(lastScoutTick<0||snapshot.Tick-lastScoutTick>=RescoutTicks))
            {
                var candidate=scout.TryPlan(observation);Choose("scout",candidate,scout.ObserveReceipt);isScout=action==candidate&&candidate!=null;
            }
            if(action==null)
            {
                var intent=new PlayableAiResearchStrategicIntent(Generation,lastScoutTick,false,opening.Intent.Tank);
                Choose("research",research.TryPlan(observation,readiness.Observe(observation,intent)),research.ObserveReceipt);
            }
            if(action==null)Choose("production",production.TryPlan(observation),production.ObserveReceipt);
            if(action!=null)
            {
                if(action.PlayerId!=OwnerId||action.Generation!=Generation||action.Seed!=observation.Seed||action.SourceIdentity!=opening.SourceIdentity||action.ProfileId!=observation.ProfileId||action.ProfileRevision!=observation.ProfileRevision||action.SnapshotSequence!=observation.SnapshotSequence)
                    throw new InvalidOperationException("AI policy returned an action outside its owner observation.");
                pending=new Pending{Observation=observation,Action=action,DueTick=snapshot.Tick+ObservationDelayTicks,HumanSequence=humanSequence,Receipt=receipt,Scout=isScout,PolicyName=policyName,Ordinal=++ordinal};
                Emit(pending,PlayableAiDeliveryStatus.Scheduled,0,null,null,null,null,"Authority scheduled.");
                Record(new PlayableAiTraceRecord(observation.Identity,action.ActionId,pending.DueTick,0,0,PlayableAiDeliveryStatus.Scheduled,null,"Authority scheduled.",observation.OwnerId,action.SourceIdentity));
            }
            else Checkpoint=Capture();
        }
        internal void Stop(long? tick=null){if(tick.HasValue)authorityTick=tick.Value;if(pending!=null)Cancel(PlayableAiDeliveryStatus.Stopped,"Runtime stopped.");}
        private void Cancel(PlayableAiDeliveryStatus status,string message)
        {var item=pending;pending=null;Finish(item,0,0,status,status==PlayableAiDeliveryStatus.Stopped?PlayableCommandStatus.Stopped:PlayableCommandStatus.Cancelled,message);}
        private void Finish(Pending item,long sequence,long applicationTick,PlayableAiDeliveryStatus status,PlayableCommandStatus runtimeStatus,string message)
        {
            var record=new PlayableAiTraceRecord(item.Observation.Identity,item.Action.ActionId,item.DueTick,sequence,
                applicationTick,status,runtimeStatus,message,item.Observation.OwnerId,item.Action.SourceIdentity);
            Emit(item,status,sequence,sequence!=0||status==PlayableAiDeliveryStatus.Stale?(long?)authorityTick:null,authorityTick,status==PlayableAiDeliveryStatus.Applied?(long?)applicationTick:null,runtimeStatus,message);
            lastPolicy=item.PolicyName;lastActionKind=item.Action.Kind;item.Receipt(record);Record(record);
#if DEVELOPMENT_BUILD
            Console.WriteLine("[Spacewars owner AI] owner="+OwnerId+" source="+PlayableAiOpeningComposition.SourceIdentity+" profile="+profile.ProfileId+"@"+profile.Revision+
                " seed="+opening.MatchSeed+" policy="+item.PolicyName+" kind="+item.Action.Kind+" status="+status+" generation="+Generation+
                " observation="+item.Observation.Identity+" action="+item.Action.ActionId+" due="+item.DueTick+" sequence="+sequence+" tick="+applicationTick+
                " commandStatus="+runtimeStatus+" decisionOrdinal="+item.Ordinal+" observationTick="+item.Observation.Tick+
                " actionTick="+item.Observation.Tick+" receiptTick="+authorityTick+" applicationTick="+
                (status==PlayableAiDeliveryStatus.Applied?applicationTick.ToString(System.Globalization.CultureInfo.InvariantCulture):"none"));
#endif
        }
        private void Emit(Pending item,PlayableAiDeliveryStatus status,long sequence,long? delivery,long? receipt,long? application,PlayableCommandStatus? runtimeStatus,string message)
        {audit?.Invoke(new PlayableAiEvaluationEvent(item.Observation,item.Action,item.PolicyName,item.Ordinal,item.DueTick,status,sequence,delivery,receipt,application,runtimeStatus,message));}
        private void Record(PlayableAiTraceRecord record)
        {records.Add(record);if(records.Count>128)records.RemoveAt(0);Checkpoint=Capture();}
        private PlayableAiOwnerCheckpoint Capture()=>new PlayableAiOwnerCheckpoint(OwnerId,PlayableAiOpeningComposition.SourceIdentity,profile.ProfileId,profile.Revision,opening.MatchSeed,Generation,lastDecisionTick,lastScoutTick,
            pending?.Action.ActionId??0,pending?.DueTick??0,lastCommandSequence,lastPolicy,lastActionKind,opening,mission.Mission,midgame.Capture(),artillery.Capture(),records);
    }
}

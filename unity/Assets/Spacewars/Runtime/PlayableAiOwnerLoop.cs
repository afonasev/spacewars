using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
#if UNITY_INCLUDE_TESTS
using Newtonsoft.Json;
#endif
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Immutable publication from the match authority. This is diagnostic evidence, not a save format.
    [Serializable]
    public sealed class PlayableAiOwnerCheckpoint
    {
        private readonly PlayableAiTraceRecord[] records;
        public PlayableAiOwnerCheckpoint(string ownerId,string sourceIdentity,string profileId,int profileRevision,int seed,long generation,long decisionTick,long lastScoutTick,long pendingActionId,long pendingDueTick,long lastCommandSequence,
            string lastPolicy,PlayableCommandKind? lastActionKind,PlayableAiOpeningCompositionState opening,PlayableAiCombatMission mission,PlayableAiMidgameCheckpoint strategy,PlayableAiArtilleryState artillery,IEnumerable<PlayableAiTraceRecord> records,AiEconomyDecision economyDecision=null,long lastScoutOrderTick=-1)
        {
            OwnerId=ownerId;SourceIdentity=sourceIdentity;ProfileId=profileId;ProfileRevision=profileRevision;Seed=seed;
            Generation=generation;DecisionTick=decisionTick;LastScoutTick=lastScoutTick;LastScoutOrderTick=lastScoutOrderTick;
            PendingActionId=pendingActionId;PendingDueTick=pendingDueTick;LastCommandSequence=lastCommandSequence;
            LastPolicy=lastPolicy;LastActionKind=lastActionKind;Opening=opening?.Restore();Mission=mission?.Copy();Strategy=strategy;Artillery=artillery;
            this.records=records.ToArray();EconomyDecision=economyDecision;
        }
        public AiPhaseCompositionState GamePhase{get;private set;}
        public AiCompositionAssessment Composition{get;private set;}
        internal PlayableAiOwnerCheckpoint BindComposition(AiPhaseCompositionState phase,AiCompositionAssessment assessment){GamePhase=phase;Composition=assessment;return this;}
        public AiDoctrineState Doctrine{get;private set;}
        internal PlayableAiOwnerCheckpoint BindDoctrine(AiDoctrineState state){Doctrine=state;return this;}
        public AiOpeningExecutionState OpeningExecution{get;private set;}
        internal PlayableAiOwnerCheckpoint BindOpeningExecution(AiOpeningExecutionState state){OpeningExecution=state;return this;}
        public AiKnowledgeState Knowledge{get;private set;}
        internal PlayableAiOwnerCheckpoint BindKnowledge(AiKnowledgeState state){Knowledge=state;return this;}
        public AiArmyMissionState ArmyMission{get;private set;}
        internal PlayableAiOwnerCheckpoint BindArmyMission(AiArmyMissionState state){ArmyMission=state;return this;}
        public AiArmyRegistryState Armies{get;private set;}
        internal PlayableAiOwnerCheckpoint BindArmies(AiArmyRegistryState state){Armies=state;return this;}
        public AiEconomyDecision EconomyDecision{get;}
        public AiInfrastructureState Infrastructure{get;private set;}
        internal PlayableAiOwnerCheckpoint BindInfrastructure(AiInfrastructureState s){Infrastructure=s;return this;}
        public AiExpansionState Expansion{get;private set;}
        internal PlayableAiOwnerCheckpoint BindExpansion(AiExpansionState state){Expansion=state;return this;}
        public string AiProfileId{get;private set;} public int AiRevision{get;private set;} public string AiHash{get;private set;}
        public AiDifficulty Difficulty{get;private set;} public uint PersonalitySeed{get;private set;}
        internal PlayableAiOwnerCheckpoint BindNativeProfile(AiProfile native,AiOwnerConfig config)
        {AiProfileId=native.Id;AiRevision=native.Revision;AiHash=native.Hash;Difficulty=config.Difficulty;PersonalitySeed=config.PersonalitySeed;return this;}
        public string OwnerId{get;} public string SourceIdentity{get;} public string ProfileId{get;} public int ProfileRevision{get;} public int Seed{get;} public long Generation{get;}
        public long DecisionTick{get;} public long LastScoutTick{get;} public long LastScoutOrderTick{get;} public long PendingActionId{get;} public long PendingDueTick{get;} public long LastCommandSequence{get;}
        public string LastPolicy{get;} public PlayableCommandKind? LastActionKind{get;}
        public PlayableAiOpeningCompositionState Opening{get;} public PlayableAiCombatMission Mission{get;}
        public PlayableAiMidgameCheckpoint Strategy{get;} public PlayableAiArtilleryState Artillery{get;}
        public IReadOnlyList<PlayableAiTraceRecord> Records=>Array.AsReadOnly(records);
    }

    // Called only by PlayableRuntime's authority thread. All policy input is a fog-safe projection.
#if UNITY_INCLUDE_TESTS
    // Immutable snapshot of values already produced by the ordinary decision path.
    internal sealed class PlayableAiProposalAdmissionReadback
    {
        internal PlayableAiProposalAdmissionReadback(string owner,long tick,string policy,PlayableAiAction action,string stage,string reason)
        {OwnerId=owner;Tick=tick;Policy=policy;Action=action;Stage=stage;Reason=reason;}
        internal string OwnerId{get;} internal long Tick{get;} internal string Policy{get;} internal PlayableAiAction Action{get;}
        internal string Stage{get;} internal string Reason{get;}
    }
    internal sealed class CurrentExpansionDecisionReadback
    {
        internal CurrentExpansionDecisionReadback(PlayableAiObservation observation,AiBudgetState ledger,int protectedFunds,
            int financeSite,bool financeReady,bool threatened,bool urgentRepair,bool allowsExpansion,
            PlayableAiAction expansionAction,PlayableAiIntentReadback expansionIntent,IEnumerable<PlayableAiIntentReadback> proposals,
            IEnumerable<string> selected,IReadOnlyDictionary<string,string> rejections,AiBudgetState beforeFinanceDetach,
            AiBudgetState afterTechnologyAllocation,AiBudgetState afterFinancialAllocation,int[] threatIds,bool missingScout,
            bool essentialScale,string scalingReason,long scalingStartup,int[] armyActors,int[] pendingActors,AiExpansionState expansionState)
        {Observation=observation;Ledger=ledger;ProtectedFunds=protectedFunds;FinanceSite=financeSite;FinanceReady=financeReady;
            Threatened=threatened;UrgentRepair=urgentRepair;AllowsExpansion=allowsExpansion;ExpansionAction=expansionAction;
            ExpansionIntent=expansionIntent;Proposals=proposals.ToArray();Selected=selected.ToArray();Rejections=new Dictionary<string,string>(rejections);
            BeforeFinanceDetach=beforeFinanceDetach;AfterTechnologyAllocation=afterTechnologyAllocation;AfterFinancialAllocation=afterFinancialAllocation;
            ThreatIds=threatIds.ToArray();MissingScout=missingScout;EssentialScale=essentialScale;ScalingReason=scalingReason;ScalingStartup=scalingStartup;
            ArmyActors=armyActors.ToArray();PendingActors=pendingActors.ToArray();ExpansionState=expansionState;}
        internal PlayableAiObservation Observation{get;} internal AiBudgetState Ledger{get;} internal int ProtectedFunds{get;}
        internal int FinanceSite{get;} internal bool FinanceReady{get;} internal bool Threatened{get;} internal bool UrgentRepair{get;}
        internal bool AllowsExpansion{get;} internal PlayableAiAction ExpansionAction{get;} internal PlayableAiIntentReadback ExpansionIntent{get;}
        internal PlayableAiIntentReadback[] Proposals{get;} internal string[] Selected{get;} internal IReadOnlyDictionary<string,string> Rejections{get;}
        internal AiBudgetState BeforeFinanceDetach{get;} internal AiBudgetState AfterTechnologyAllocation{get;} internal AiBudgetState AfterFinancialAllocation{get;}
        internal int[] ThreatIds{get;} internal bool MissingScout{get;} internal bool EssentialScale{get;} internal string ScalingReason{get;} internal long ScalingStartup{get;}
        internal int[] ArmyActors{get;} internal int[] PendingActors{get;} internal AiExpansionState ExpansionState{get;}
    }
    internal sealed partial class PlayableAiOwnerLoop
    {
        internal static Action<CurrentExpansionDecisionReadback> CurrentExpansionDecisionReadbackForTests;
        internal static string CurrentExpansionDecisionOwnerFilterForTests;
        internal static Action<PlayableAiProposalAdmissionReadback> ProposalAdmissionReadbackForTests;
        internal static string ProposalAdmissionOwnerFilterForTests;
        internal static Action<string> CurrentSafeFundingReadbackForTests;
        internal static string CurrentSafeFundingOwnerFilterForTests;
        internal static Func<string,string,bool> CurrentSafeFundingStageFilterForTests;
        internal static Action<string,long,PlayableAiObservation,AiArmyRegistryState,PlayableAiAction[]> CurrentOwnerDecisionReadbackForTests;
        internal static string CurrentOwnerDecisionOwnerFilterForTests;
    }
#endif
#if UNITY_INCLUDE_TESTS
    internal sealed class PlayableAiIntentReadback
    {
        private readonly string[] claims;
        internal PlayableAiIntentReadback(AiIntent intent,string armyRejection)
        {if(intent==null)return;Id=intent.Id;Policy=intent.Policy;Action=intent.Action;ExpiresTick=intent.ExpiresTick;Credits=intent.Credits;
            Population=intent.Population;claims=intent.Claims.ToArray();Priority=intent.Priority;StartupFund=intent.StartupFund;
            ReservationId=intent.ReservationId;Conditions=intent.Conditions;ArmyRejection=armyRejection;}
        public string Id{get;} public string Policy{get;} public PlayableAiAction Action{get;} public long ExpiresTick{get;}
        public int Credits{get;} public int Population{get;} public int Priority{get;} public long StartupFund{get;}
        public string ReservationId{get;} public string Conditions{get;} public string ArmyRejection{get;}
        public string[] Claims=>claims?.ToArray()??Array.Empty<string>();
    }
    // Detached, test-only preselection evidence captured after the actual expansion proposal is
    // assembled and before arbitration. The planner fork exists only for isolated counterfactual probes.
    internal sealed class PlayableAiPreselectionReadback
    {
        private readonly int[] armyActors,pendingActors;
        internal PlayableAiPreselectionReadback(PlayableAiObservation observation,AiBudgetState ledger,int protectedFunds,
            int financeSite,bool financeReady,bool threatened,bool urgentRepair,bool allowsExpansion,long lastDecisionTick,
            long decisionIntervalTicks,long observationDelayTicks,IEnumerable<int> armyActors,IEnumerable<int> pendingActors,
            AiExpansionState expansionState,AiExpansionPlanner expansionProbe,AiKnowledgeState knowledge,IEnumerable<PlayableAiTraceRecord> trace,
            PlayableAiAction counterfactualAlternative,PlayableAiIntentReadback counterfactualProposal,bool pendingExpansion,int availableActions,
            PlayableAiAction actualExpansionAction,PlayableAiIntentReadback actualExpansionProposal)
        {Observation=observation;Ledger=ledger;ProtectedFunds=protectedFunds;FinanceSite=financeSite;FinanceReady=financeReady;Threatened=threatened;
            UrgentRepair=urgentRepair;AllowsExpansion=allowsExpansion;LastDecisionTick=lastDecisionTick;DecisionIntervalTicks=decisionIntervalTicks;
            ObservationDelayTicks=observationDelayTicks;this.armyActors=armyActors.OrderBy(x=>x).ToArray();this.pendingActors=pendingActors.OrderBy(x=>x).ToArray();
            ExpansionState=expansionState;ExpansionProbe=expansionProbe;Knowledge=knowledge;Trace=trace.ToArray();
            CounterfactualAlternative=counterfactualAlternative;CounterfactualProposal=counterfactualProposal;PendingExpansion=pendingExpansion;AvailableActions=availableActions;
            ActualExpansionAction=actualExpansionAction;ActualExpansionProposal=actualExpansionProposal;}
        internal PlayableAiObservation Observation{get;} internal AiBudgetState Ledger{get;} internal int ProtectedFunds{get;}
        internal int FinanceSite{get;} internal bool FinanceReady{get;} internal bool Threatened{get;} internal bool UrgentRepair{get;}
        internal bool AllowsExpansion{get;} internal long LastDecisionTick{get;} internal long DecisionIntervalTicks{get;}
        internal long ObservationDelayTicks{get;} internal AiExpansionState ExpansionState{get;} internal AiExpansionPlanner ExpansionProbe{get;}
        internal AiKnowledgeState Knowledge{get;} internal PlayableAiTraceRecord[] Trace{get;} internal int[] ArmyActors=>armyActors.ToArray();internal int[] PendingActors=>pendingActors.ToArray();
        internal PlayableAiAction CounterfactualAlternative{get;} internal PlayableAiIntentReadback CounterfactualProposal{get;}
        internal bool PendingExpansion{get;} internal int AvailableActions{get;}
        internal PlayableAiAction ActualExpansionAction{get;} internal PlayableAiIntentReadback ActualExpansionProposal{get;}
        internal bool FreshActor(int id)=>!armyActors.Contains(id)&&!pendingActors.Contains(id);
    }
#endif
    internal sealed partial class PlayableAiOwnerLoop
    {
#if UNITY_INCLUDE_TESTS
        internal static Action<PlayableAiPreselectionReadback> PreselectionReadbackForTests;
        internal static string PreselectionReadbackOwnerFilterForTests;
        internal static long PreselectionReadbackElapsedStopwatchTicksForTests;
        internal static int PreselectionReadbackOwnerCallsForTests;
#endif
        internal string OwnerId{get;}
        private long DecisionIntervalTicks;
        private long ObservationDelayTicks;
        private sealed class Pending
        {
            public PlayableAiObservation Observation;
            public string ObservationIdentity;public long ObservationTick,SnapshotSequence;
            public PlayableAiAction Action;
            public long DueTick,HumanSequence,Ordinal;
            public Action<PlayableAiTraceRecord> Receipt;
            public bool Scout;
            public string PolicyName;
            public AiIntent Intent;public AiReceiptIdentity Identity;public long LocalActionId;
        }
        private PlayableProfile profile;
        internal void Rebind(PlayableProfile next){if(WorldWire.Binding(profile).SequenceEqual(WorldWire.Binding(next)))return;while(pending!=null)Cancel(PlayableAiDeliveryStatus.Cancelled,"Balance revision changed.");var previousFinancial=expansionFinancial.Detach(budget);profile=next;arbiter.ClearPositionSaving();armyPlanner.Rebind(next);flankPlanner.Rebind(next);foreach(var witness in budget.Capture().Reserved.Where(AiEconomicInvestmentPlanner.IsDemand))budget.Release(witness.Id);budget.RepriceReservations(e=>launches.IsStartup(e)?launches.Reprice(e,next):infrastructure.IsConversion(e)?infrastructure.Reprice(e,next):AiTechnologyFunding.IsFunding(e)?technologyFunding.Reprice(e,next):AiEconomicInvestmentPlanner.IsDemand(e)?0:BudgetCost(e.Action),BudgetTerms);infrastructure.Rebind(next,budget,authorityTick);technologyFunding.Rebind(next,nativeProfile,budget,arbiter);launches.ReconcileFunding(budget,next,authorityTick);expansion.ReconcileFunding(budget,next,authorityTick,pendingItems.Select(x=>x.Intent));expansionFinancial.Rebind(next,nativeProfile,authorityTick,budget,arbiter,previousFinancial);production.Rebind(next);mission.Rebind(next);midgame.Rebind(next);artillery.Rebind(next);Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);}
        private AiKnowledgeTracker knowledge;
        private AiProfile nativeProfile;
        private readonly AiRosterCatalog roster=AiRosterCatalog.Initial;
        private AiOwnerConfig nativeConfig;
        private readonly PlayableAiOpeningCompositionState opening;
        private readonly AiOpeningExecutor openingExecutor;
        private readonly AiDoctrinePolicy doctrine;
        internal int ProtectedCapturerId=>openingExecutor.ProtectedUnit;
        private PlayableAiEconomicLivenessPolicy economy;
        internal AiEconomyPlan EconomyPlan{get;private set;}
        private AiEconomyDecision economyDecision;
        private PlayableAiProductionLivenessPolicy production;
        private PlayableAiResearchLivenessPolicy research;
        private readonly AiTechnologyPlanner technology=new AiTechnologyPlanner();
        private AiTechnologyFunding technologyFunding;
        public AiTechnologyPlan TechnologyPlan{get;private set;}
        private PlayableAiScoutLivenessPolicy scout;
        private readonly AiScoutPlanner scoutPlanner=new AiScoutPlanner();
        private int ScoutBudget=>!openingExecutor.Air&&openingExecutor.Capture().Phase==PlayableAiOpeningPhase.Active?0:scoutPlanner.Budget(nativeProfile,nativeConfig.Difficulty,nativeConfig.PersonalitySeed,openingExecutor.ScoutPlan);
        private PlayableAiMissionDefensePolicy mission;
        private readonly PlayableAiMidgameStrategyPolicy midgame;
        private PlayableAiArtillerySupportPolicy artillery;
        private readonly List<PlayableAiTraceRecord> records=new List<PlayableAiTraceRecord>();
        private readonly List<Pending> pendingItems=new List<Pending>();
        private Pending pending=>pendingItems.FirstOrDefault();
        private readonly AiDecisionArbiter arbiter;
        private AiBudgetLedger budget;
        private AiArmyRegistry armies;
        private AiArmyPlanner armyPlanner;
        private readonly AiArmyPlanner flankPlanner;
        private readonly HashSet<int> utilityReserved=new HashSet<int>();
        private AiDefensePlanner defense;
        private AiPhaseCompositionPlanner composition;
        private AiCompositionAssessment compositionAssessment;
        private AiProductionDemand demand=new AiProductionDemand();
        private AiFactoryLaunchCommitments launches=new AiFactoryLaunchCommitments();
        private AiExpansionPlanner expansion;
        private AiExpansionFinancial expansionFinancial;
        private AiInfrastructurePlanner infrastructure;
        internal AiBudgetLedger Budget=>budget;
        private string BudgetTerms=>profile.ProfileId+"@"+profile.Revision;
        internal void ReconcileBudget(PlayableDomain domain)=>domain.ReconcileAiPayments(budget);
        internal void ValidateBudget(PlayableDomain domain)
        {launches.Validate(budget,pendingItems.Select(x=>x.Intent).ToArray(),pendingItems.Select(x=>x.Identity).ToArray(),profile,domain.Tick);expansion.ValidateFunding(budget,pendingItems.Select(x=>x.Intent).ToArray(),profile,Generation);infrastructure.ValidateFunding(budget,pendingItems.Select(x=>x.Intent).ToArray(),profile,domain.Tick);technologyFunding.Validate(domain.TechnologyFundingValidationView(OwnerId,opening.MatchSeed),profile,budget,arbiter,pendingItems.Select(x=>x.Intent).ToArray());AiStateWire.Require(pendingItems.Where(x=>x.Intent.ReservationId!=null).All(x=>x.PolicyName=="production"||x.PolicyName=="expansion"||x.PolicyName=="infrastructure"||x.Intent.ReservationId.StartsWith(AiTechnologyFunding.Purpose+":",StringComparison.Ordinal)&&(x.PolicyName=="economy"||x.PolicyName=="research")),"unknown reservation policy");expansionFinancial.Validate(profile,nativeProfile,budget,arbiter,domain.Tick,opening.MatchSeed,domain.FinancialPublicCatalog,domain.OwnerIds.Count());expansionFinancial.ValidateHome(arbiter,domain.FinancialOwnerHome(OwnerId));AiEconomicInvestmentPlanner.ValidateDemand(budget,profile,domain.Tick);domain.ValidateAiBudget(budget);domain.ValidateAiProductionDemand(demand,OwnerId);domain.ValidateAiArmies(armies);domain.ValidateAiKnowledge(knowledge.Capture());}
        private int actionLimit;
        private long repeatTicks;
        private Func<long> allocateAction;
        internal void BindActionAllocator(Func<long> allocator){allocateAction=allocator;}
        private long lastScoutOrderTick=-1;
        private long lastDecisionTick,lastScoutTick=-1,lastCommandSequence,ordinal,authorityTick;
        private readonly Action<PlayableAiEvaluationEvent> audit;
        private string lastPolicy;
        private PlayableCommandKind? lastActionKind;

        internal PlayableAiOwnerLoop(PlayableProfile profile,PlayableAiOpeningCompositionState opening,long generation,Action<PlayableAiEvaluationEvent> audit=null)
            :this(profile,opening,generation,audit,null,AiDifficulty.Fighter){}
        internal PlayableAiOwnerLoop(PlayableProfile profile,PlayableAiOpeningCompositionState opening,long generation,Action<PlayableAiEvaluationEvent> audit,AiProfile aiProfile,AiDifficulty aiDifficulty)
        :this(profile,opening,generation,audit,aiProfile,aiDifficulty,false){}
        private PlayableAiOwnerLoop(PlayableProfile profile,PlayableAiOpeningCompositionState opening,long generation,Action<PlayableAiEvaluationEvent> audit,AiProfile aiProfile,AiDifficulty aiDifficulty,bool restoring)
        {
            this.audit=audit;
            nativeProfile=aiProfile??AiProfile.Initial;
            DecisionIntervalTicks=AiProfile.SecondsToTicks(nativeProfile.DifficultyValue(aiDifficulty,"decisionSeconds"),30);
            ObservationDelayTicks=AiProfile.SecondsToTicks(nativeProfile.DifficultyValue(aiDifficulty,"reactionSeconds"),30);
            this.profile=profile??throw new ArgumentNullException(nameof(profile));
            this.opening=opening??throw new ArgumentNullException(nameof(opening));
            if(!PlayableAiOpeningComposition.HasNativeBinding(opening)||!restoring&&opening.ProfileIdentity!=PlayableAiOpeningComposition.ProfileBinding(nativeProfile))throw new ArgumentException("Owner/native opening profile/RNG mismatch.",nameof(opening));
            AiCombatMicroSafety.PrepareExecutionAssets(this.profile);
            OwnerId=opening.OwnerId;doctrine=new AiDoctrinePolicy(opening,generation);composition=new AiPhaseCompositionPlanner(OwnerId,generation,roster){Doctrine=doctrine};openingExecutor=new AiOpeningExecutor(opening,generation){PlayableProfileForObservation=profile};knowledge=new AiKnowledgeTracker(OwnerId,generation);
            nativeConfig=new AiOwnerConfig(OwnerId,aiDifficulty,opening.MatchSeed,nativeProfile);
            arbiter=new AiDecisionArbiter(nativeProfile);actionLimit=(int)nativeProfile.DifficultyValue(aiDifficulty,"actionsPerDecision");
            repeatTicks=AiProfile.SecondsToTicks(nativeProfile.Value("decision.repeatOrderSeconds"),30);
            Generation=generation;mission=new PlayableAiMissionDefensePolicy(profile,ownerId:OwnerId);midgame=new PlayableAiMidgameStrategyPolicy(profile,OwnerId);artillery=new PlayableAiArtillerySupportPolicy(profile,ownerId:OwnerId);
            budget=new AiBudgetLedger(OwnerId,generation,AiProfile.SecondsToTicks(nativeProfile.Value("economy.reservationExpirySeconds"),30));
            armies=new AiArmyRegistry(OwnerId,generation,nativeProfile,aiDifficulty);
            armyPlanner=new AiArmyPlanner(OwnerId,generation);flankPlanner=new AiArmyPlanner(OwnerId,generation,AiArmyPlanner.FlankPolicy);flankPlanner.Rebind(profile);armyPlanner.Rebind(profile);doctrine.CombatCountable=id=>!utilityReserved.Contains(id)&&!expansion.ClaimsActor(id)&&!armies.IsScoutMember(id);doctrine.UtilityAvailable=id=>armies.ArmyFor(id)==0||armies.IsScoutMember(id);defense=new AiDefensePlanner(OwnerId,generation);technologyFunding=new AiTechnologyFunding(OwnerId,generation);expansionFinancial=new AiExpansionFinancial(OwnerId,generation);
            expansion=new AiExpansionPlanner(OwnerId);infrastructure=new AiInfrastructurePlanner(OwnerId);
            if(!restoring)expansionFinancial.InitializeNewMatch(profile,nativeProfile,arbiter);
            economy=new PlayableAiEconomicLivenessPolicy(ownerId:OwnerId);research=new PlayableAiResearchLivenessPolicy(ownerId:OwnerId);scout=new PlayableAiScoutLivenessPolicy(ownerId:OwnerId);
            production=new PlayableAiProductionLivenessPolicy(profile:profile,ownerId:OwnerId,catalog:roster);
            Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);
        }
        internal long Generation{get;}
        internal PlayableAiOwnerCheckpoint Checkpoint{get;private set;}
        internal void Deliver(PlayableDomain domain,long humanSequence,bool paused)
        {
            ReconcileBudget(domain);
            // One bounded scan of the current pending set. A rate/fairness/path
            // wait at the front must not hide a ready opposite-category order.
            foreach(var item in pendingItems.ToArray())
                if(pendingItems.Contains(item))DeliverSelected(domain,humanSequence,paused,item,true);
        }
        internal void DeliverReady(PlayableDomain domain,long humanSequence,bool paused,bool coverageReady)
        {
            if(coverageReady){Deliver(domain,humanSequence,paused);return;}
            ReconcileBudget(domain);
            // A waiting offensive proof must not block defense or lifecycle work.
            foreach(var item in pendingItems.ToArray())
                if(pendingItems.Contains(item))DeliverSelected(domain,humanSequence,paused,item,false);
        }
        private void DeliverOne(PlayableDomain domain,long humanSequence,bool paused)
            =>DeliverSelected(domain,humanSequence,paused,pending,true);
        private void DeliverSelected(PlayableDomain domain,long humanSequence,bool paused,Pending pending,bool coverageReady)
        {
            authorityTick=domain.Tick;
            if(!domain.Authorizes(OwnerId)){Stop(domain.Tick);return;}
            if(pending==null)return;
            if(paused){CancelItem(pending,PlayableAiDeliveryStatus.Cancelled,"Runtime paused.");return;}
            if(domain.Outcome!=PlayableMatchOutcome.Playing){CancelItem(pending,PlayableAiDeliveryStatus.Stopped,"Match finished.");return;}
            if(humanSequence!=pending.HumanSequence){CancelItem(pending,PlayableAiDeliveryStatus.Cancelled,"Newer human command has priority.");return;}
            if(domain.Tick<pending.DueTick)return;
            var item=pending;int itemIndex=pendingItems.IndexOf(item);pendingItems.Remove(item);
            var action=item.Action;
            if(domain.Tick>item.Intent.ExpiresTick){Finish(item,0,0,PlayableAiDeliveryStatus.Stale,PlayableCommandStatus.StaleGeneration,"Intent expired before delivery.");return;}
            if(item.Intent.Conditions?.StartsWith(AiCombatMicroPolicy.PendingRepairHandoff,StringComparison.Ordinal)==true){
                var current=PlayableAiObservation.From(domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId)));
                if(!combatMicro.CanYieldPendingRepair(current,profile,nativeProfile,nativeConfig.Difficulty,armies,item.PolicyName,action))
                {Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Pending local repair changed before army handoff.");return;}
            }
            if(action.Generation!=Generation||action.PlayerId!=OwnerId||action.Seed!=opening.MatchSeed||action.SourceIdentity!=opening.SourceIdentity||action.ProfileId!=profile.ProfileId||action.ProfileRevision!=profile.Revision||action.SnapshotSequence!=item.SnapshotSequence)
            {
                Finish(item,0,0,PlayableAiDeliveryStatus.Stale,PlayableCommandStatus.StaleGeneration,"Observation binding changed.");return;
            }
            if(item.PolicyName==AiCombatMicroPolicy.Policy)BindCombatMicro(PlayableAiObservation.From(domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId))));
            bool preemptMicro=item.PolicyName!=AiCombatMicroPolicy.Policy&&MicroReserved(action)&&item.Intent.Priority>=2;
            if(item.PolicyName!=AiCombatMicroPolicy.Policy&&MicroReserved(action)&&!preemptMicro)
            {Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Current local micro still owns the recipients.");return;}
            string ownership=armies.Reject(item.Intent);
            if(ownership!=null){Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,ownership);return;}
            if(!coverageReady&&(Offensive(item)||Technology(item))){pendingItems.Insert(itemIndex,item);return;}
            PlayableAiObservation repairDelivery=null;
            try{
            var deliveryPlanner=item.PolicyName==AiArmyPlanner.FlankPolicy?flankPlanner:armyPlanner;
            var deliveryMission=deliveryPlanner.Capture();
            if(AiArmyPlanner.IsArmyPolicy(item.PolicyName)&&(deliveryMission.Reason==AiReinforcementPolicy.RegroupReason||
                (action.Kind==PlayableCommandKind.Follow||action.Kind==PlayableCommandKind.Attack||action.Kind==PlayableCommandKind.AttackMove)&&
                armies.CaptureArmy(deliveryMission.ArmyId).Any(a=>action.EntityIds.Any(a.Reinforcements.Contains))))
            {
                var reinforcementView=PlayableAiObservation.From(domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId)));
                if(!deliveryPlanner.CanDeliverReinforcement(reinforcementView,profile,nativeProfile,armies,action))
                {Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Receiving core changed before reinforcement delivery.");return;}
            }
            if((AiArmyPlanner.IsArmyPolicy(item.PolicyName)||item.PolicyName==AiDefensePlanner.Policy)&&(action.Kind==PlayableCommandKind.Move||action.Kind==PlayableCommandKind.Follow))
            {
                bool foreignLeader=action.Kind==PlayableCommandKind.Follow&&(armies.ArmyFor(action.TargetId)==0||action.EntityIds.Any(id=>armies.ArmyFor(id)!=armies.ArmyFor(action.TargetId))||armies.Capture().Armies.Single(a=>a.Id==armies.ArmyFor(action.TargetId)).Reinforcements.Contains(action.TargetId));
                bool withdrawal=action.Kind==PlayableCommandKind.Move&&deliveryMission.Phase==AiArmyPhase.Retreating&&deliveryMission.Reason=="withdrawal"&&action.EntityIds.All(id=>armies.ArmyFor(id)==deliveryMission.ArmyId&&!armies.Capture().Armies.Single(a=>a.Id==deliveryMission.ArmyId).Reinforcements.Contains(id));
                withdrawal|=action.Kind==PlayableCommandKind.Move&&deliveryMission.Reason==AiReinforcementPolicy.RegroupReason;
                if(AiArmyPlanner.IsArmyPolicy(item.PolicyName)&&deliveryPlanner.OwnsPendingRepairReturn(action,item.LocalActionId))
                {
                    repairDelivery=PlayableAiObservation.From(domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId)));
                    if(!deliveryPlanner.CanDeliverRepairReturn(repairDelivery,profile,nativeProfile,armies,action,item.LocalActionId))
                    {Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Repair return certificate changed before delivery.");return;}
                    withdrawal=true;
                }
                string rejection=foreignLeader?"leader registry changed":domain.ArmyMoveRejection(action,withdrawal);
                if(rejection!=null){Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Current observed army path rejected: "+rejection+".");return;}
            }
            if(item.PolicyName==AiCombatMicroPolicy.Policy)
            {
                string rejection=domain.CombatMicroRejection(action,combatMicro.PendingPhase,knowledge.Capture(),nativeProfile,nativeConfig.Difficulty,combatMicro);
                if(rejection!=null){Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,rejection);return;}
            }
            }catch(ObservedPathPending){pendingItems.Insert(0,item);return;}

            // Pending tactical proposals can become redundant during their reaction delay.
            // Check the actual owner projection before allocating a command sequence.
            if(Tactical(action))
            {
                var delivery=repairDelivery??PlayableAiObservation.From(domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId)));
                var recipients=delivery.Entities.Where(u=>u.Owner==delivery.Owner&&u.Health>0&&action.EntityIds.Contains(u.Id)).ToArray();
                if(item.PolicyName==AiDefensePlanner.Policy&&action.Kind==PlayableCommandKind.Stop&&
                    (recipients.Length!=1||!AiTacticalExecutor.IsCurrentAppliedDefenseAssemblyOrder(delivery,recipients[0],records)))
                {Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Defense assembly order changed before its safety Stop was due.");return;}
                if(recipients.Length==action.EntityIds.Count&&recipients.Length>0&&recipients.All(u=>SameOrder(action,u,item.PolicyName,item.PolicyName==AiArmyPlanner.FlankPolicy?flankPlanner.HasAppliedOrder:armyPlanner.HasAppliedOrder,OwnerId,Generation)))
                {Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Effective tactical order already matches at delivery.");return;}
            }
            if(!CombatAttentionAllows(domain,item,itemIndex))return;
            long sequence=domain.AllocateAiSequence();lastCommandSequence=sequence;
            var command=new PlayableCommand(action.Generation,sequence,action.PlayerId,action.Kind,action.CopyEntityIds(),action.Target,
                siteId:action.SiteId,slotId:action.SlotId,buildingKind:action.BuildingKind,parentId:action.ParentId,targetId:action.TargetId,
                productionOrderId:action.ProductionOrderId,unitKind:action.UnitKind,researchKind:action.ResearchKind,origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:action.ActionId,actionId:action.ActionId);
            PlayableCommandStatus status;string message;
            try{status=domain.Apply(command,out message);}
            catch(Exception ex){
                if(ex is PlayableDomain.TacticalCommitFailure committed&&committed.Phase==PlayableDomain.TacticalCommitPhase.Committed){
                    // The accepted replacement survives a failed postcommit observer.
                    if(preemptMicro){var secondary=PreemptCombatMicro(action);if(secondary!=null)PlayableDomain.PreserveTacticalSecondary(ex,secondary);}
                    try{Finish(item,sequence,domain.Tick,PlayableAiDeliveryStatus.Rejected,PlayableCommandStatus.Rejected,ex.ToString());}
                    catch(Exception secondary){PlayableDomain.PreserveTacticalSecondary(ex,secondary);}
                }else Finish(item,sequence,domain.Tick,PlayableAiDeliveryStatus.Rejected,PlayableCommandStatus.Rejected,ex.ToString());
                throw;
            }
            if(status==PlayableCommandStatus.Applied)
            {
                Exception failure=preemptMicro?PreemptCombatMicro(action):null;
                void Attempt(Action operation){try{operation();}catch(Exception error){if(failure==null)failure=error;else PlayableDomain.PreserveTacticalSecondary(failure,error);}}
                Attempt(()=>combatAttention.Charge(domain.Tick,nativeProfile,nativeConfig.Difficulty,item.PolicyName==AiCombatMicroPolicy.Policy));
                if(item.PolicyName==AiCombatMicroPolicy.Policy)Attempt(()=>combatMicro.Applied(action,sequence,domain.Tick,item.Identity));
                Attempt(()=>domain.BindAiRepairPayment(action,item.Identity));
                Attempt(()=>domain.RecordBotCameraActivity(command));
                // Preserve command cadence as a separate receipt clock, never intel success.
                if(item.Scout&&item.Action.Kind==PlayableCommandKind.Move)lastScoutOrderTick=domain.Tick;
                Attempt(()=>budget.ObserveBank(domain.AiLiquidCredits(OwnerId)));
                Attempt(()=>Finish(item,sequence,domain.Tick,PlayableAiDeliveryStatus.Applied,status,message,domain.AiCommandPaid(action)));
                if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                return;
            }

            budget.ObserveBank(domain.AiLiquidCredits(OwnerId));
            Finish(item,sequence,domain.Tick,PlayableAiDeliveryStatus.Rejected,status,message,domain.AiCommandPaid(action));
        }
        internal sealed class ReviewCloneInput
        {
            private readonly byte[] bytes;private readonly PlayableProfile profile;private readonly AiProfile native;
            private readonly long generation,tick;private readonly int seed;private readonly AiTeamAssignment assignment;
            internal ReviewCloneInput(byte[] bytes,PlayableProfile profile,AiProfile native,long generation,int seed,long tick,AiTeamAssignment assignment)
            {this.bytes=bytes;this.profile=profile;this.native=native;this.generation=generation;this.seed=seed;this.tick=tick;this.assignment=assignment;}
            internal PlayableAiOwnerLoop Restore()
            {
                using(var stream=new System.IO.MemoryStream(bytes,false))
                using(var reader=new System.IO.BinaryReader(stream)){
                    var clone=ReadState(reader,profile,native,generation,seed,tick,reviewClone:true);
                    if(stream.Position!=stream.Length)throw new InvalidOperationException("Trailing owner review state.");
                    clone.BindTeamAssignment(CopyReviewAssignment(assignment));return clone;
                }
            }
        }
        private static AiTeamAssignment CopyReviewAssignment(AiTeamAssignment value)=>value==null?null:new AiTeamAssignment{
            TargetId=value.TargetId,SecondaryTargetId=value.SecondaryTargetId,Helping=value.Helping,WaitUntilTick=value.WaitUntilTick,Reason=value.Reason,
            GreedyAllies=new HashSet<string>(value.GreedyAllies,StringComparer.Ordinal),DeniedSites=new HashSet<int>(value.DeniedSites)};
        internal ReviewCloneInput CaptureReviewInput(long tick)=>new ReviewCloneInput(WorldWire.Pack(WriteState),profile,nativeProfile,Generation,opening.MatchSeed,tick,CopyReviewAssignment(teamAssignment));
        internal PlayableAiOwnerLoop CreateReviewClone(long tick)=>CaptureReviewInput(tick).Restore();
        // Only current own-unit membership crosses back from the world. Decisions
        // remain bound to their original fog-safe observation and normal delivery guards.
        internal void ReconcileReviewMembership(PlayableSnapshot current)
        {
            authorityTick=current.Tick;
            var view=PlayableAiObservation.From(current);
            var live=new HashSet<int>(view.Entities.Where(u=>u.Owner==view.Owner&&u.Health>0).Select(u=>u.Id));
            foreach(var item in pendingItems.Where(p=>p.Action.EntityIds.Any(id=>!live.Contains(id))&&Tactical(p.Action)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,current.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Observed recipients are no longer alive.");}
            armies.Observe(view);armyPlanner.ReconcileRegistry(armies,current.Tick);flankPlanner.ReconcileRegistry(armies,current.Tick);defense.ReconcileRegistry(armies);
            Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);
        }
        internal bool MergeReviewBudget(AiBudgetState initial,AiBudgetState current,long tick)
        {
            if(!expansionFinancial.ReviewClocksCurrent(budget,arbiter,tick))return false;
            if(!AiBudgetLedger.TryMergeReview(initial,budget.Capture(),current,tick,out var merged))return false;
            budget=merged;Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);return true;
        }
        internal void ReconcileReviewClocks(long tick)=>expansionFinancial.ReconcileReviewClocks(budget,arbiter,tick);
        internal long StartupReviewTick=>DecisionIntervalTicks;
        internal PlayableAiOwnerLoop CreateStartupWarmup()=>new PlayableAiOwnerLoop(profile,
            PlayableAiOpeningComposition.Initialize(opening.MatchSeed,OwnerId,aiProfile:nativeProfile),
            Generation,null,nativeProfile,nativeConfig.Difficulty);
        internal AiArmyRegistryState ArmyState=>armies.Capture();
        internal AiProfile TeamProfile=>nativeProfile;
        private AiTeamAssignment teamAssignment;
        internal void BindTeamAssignment(AiTeamAssignment assignment){teamAssignment=assignment;openingExecutor.KnownGreedyAllies=assignment?.GreedyAllies;armyPlanner.TeamAssignment=assignment;flankPlanner.TeamAssignment=assignment==null?null:new AiTeamAssignment{TargetId=assignment.SecondaryTargetId,Helping=assignment.SecondaryTargetId>0,Reason=assignment.SecondaryTargetId>0?"allied secondary defense":"independent second direction"};expansion.TeamDeniedSites=assignment?.DeniedSites;}
        internal AiTeamPublication PublishTeam(PlayableAiObservation o)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.TeamPublish);
            var active=expansion.Capture();
            var homeThreats=AiDefensePlanner.Threats(o,profile);
            bool homeThreatened=homeThreats.Length>0;
            bool canExpand=!expansionFinancial.BootstrapActive(arbiter)&&!homeThreatened;
            var intel=knowledge.Capture();
            // Publish only voluntary objectives, never the private balance/queues.
            var sites=RequestedTeamSites(o,intel,canExpand);
            return new AiTeamPublication{Owner=OwnerId,Opening=opening.InitialOpening,Team=o.Participants.Count==0?(int)o.Owner:o.Team,Tick=o.Tick,Mission=armyPlanner.Capture(),Targets=AiOpponentSelector.Assess(o,profile,nativeProfile,armyPlanner.Capture().TargetId),
                ExpansionSite=active.SiteId,ExpansionExpires=active.ExpiresTick,RequestedSites=sites,ThreatTargets=homeThreats.Select(t=>t.Id).OrderBy(id=>id).ToArray(),HomeThreatened=homeThreatened};
        }

        private int[] RequestedTeamSites(PlayableAiObservation o,AiKnowledgeState intel,bool canExpand)
        {
            // All predicates read this detached observation. Reject ineligible sites
            // before the full nearest-safe-site/remote-territory calculation; retain
            // the same filters, actor checks and final ordering.
            return o.Sites.Where(s=>canExpand&&s.CenterId==0&&!s.Contested&&AiIncomeSiteSelection.Available(s,o)&&
                (s.Site.Kind==PlayableBuildingKind.Mine||s.Site.Kind==PlayableBuildingKind.Outpost||s.Site.Kind==PlayableBuildingKind.RelayTower)&&TerritoryRules.Income(profile,s.Site.Kind)>0&&o.Credits>=TerritoryRules.Cost(profile,s.Site.Kind)&&
                AiIncomeSiteSelection.TerritoryAllowed(o,profile,nativeProfile,intel,s)&&AiIncomeSiteSelection.SiteSafe(o,profile,intel,s))
                .Where(s=>o.Entities.Any(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0&&!u.Moving&&u.CurrentOrder==null&&armies.ArmyFor(u.Id)==0&&!pendingItems.Any(i=>i.Action.EntityIds.Contains(u.Id))&&
                    o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.TargetId==s.Site.Id&&r.Kind==PlayableRouteTargetKind.PublicObjective)))
                .OrderBy(s=>o.Entities.Where(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0).Select(u=>AiOpponentSelector.Distance(u.Position,s.Site.Position)).DefaultIfEmpty(double.MaxValue).Min()).ThenBy(s=>s.Site.Id).Select(s=>s.Site.Id).ToArray();
        }

        internal bool NeedsArmyRoutes(long tick)=>(pendingItems.Any(Technology))||defense.Active&&(pendingItems.Any(Offensive)||technologyFunding.HasHold(budget))||tick-lastDecisionTick>=DecisionIntervalTicks||
            armies.HasReinforcements&&records.Any(r=>AiArmyPlanner.IsArmyPolicy(r.Policy)&&r.Status==PlayableAiDeliveryStatus.Applied&&
                (r.Kind==PlayableCommandKind.Attack||r.Kind==PlayableCommandKind.AttackMove)&&r.ApplicationTick==tick-1);
        internal bool NeedsDefenseRoutes(PlayableSnapshot snapshot)=>(!defense.EmergencyObserved||technologyFunding.HasHold(budget)||pendingItems.Any(Offensive))&&AiDefensePlanner.Threats(PlayableAiObservation.From(snapshot),profile).Length>0;
        private static bool Offensive(Pending p)=>AiArmyPlanner.IsArmyPolicy(p.PolicyName)||p.PolicyName=="artillery"||p.PolicyName=="expansion";
        private static bool Technology(Pending p)=>p.PolicyName=="research"||p.Action.Kind==PlayableCommandKind.BuildAt&&p.Action.BuildingKind==PlayableBuildingKind.ScientificCenter;
        internal bool CanFundTechnology(PlayableAiObservation o,bool urgentRepair,bool essentialScale)
        {
#if UNITY_INCLUDE_TESTS
            if(FundingReadbacksEnabledFor("CanFundTechnology"))
            {
                bool emergency=TechnologyEmergency(o);bool? missingOpening=null;
                bool allowed=!emergency&&!urgentRepair&&!essentialScale&&!(bool)(missingOpening=openingExecutor.MissingEssentialEconomy(o,nativeProfile));
                FundingReadback("CanFundTechnology",new{Emergency=emergency,UrgentRepair=urgentRepair,EssentialScale=essentialScale,MissingOpeningEconomy=missingOpening,Allowed=allowed});
                return allowed;
            }
#endif
            return !TechnologyEmergency(o)&&!urgentRepair&&!essentialScale&&!openingExecutor.MissingEssentialEconomy(o,nativeProfile);
        }
        private bool TechnologyEmergency(PlayableAiObservation o)
        {
#if UNITY_INCLUDE_TESTS
            var threats=AiDefensePlanner.Threats(o,profile);bool? covered=threats.Length>0?defense.CoversTechnologyThreats(o,profile,nativeProfile,armies,records):(bool?)null;
            bool emergency=threats.Length>0&&!covered.Value;
            if(FundingReadbacksEnabledFor("TechnologyEmergency"))FundingReadback("TechnologyEmergency",new{ThreatIds=threats.Select(t=>t.Id).ToArray(),Coverage=covered.HasValue?(covered.Value?"covered":"uncovered"):"no-threats/not-evaluated",Emergency=emergency});
            return emergency;
#else
            return AiDefensePlanner.Threats(o,profile).Length>0&&!defense.CoversTechnologyThreats(o,profile,nativeProfile,armies,records);
#endif
        }
#if UNITY_INCLUDE_TESTS
        private void FundingReadback(string stage,object data)
        {if(FundingReadbacksEnabledFor(stage))CurrentSafeFundingReadbackForTests(JsonConvert.SerializeObject(new{Owner=OwnerId,Tick=authorityTick,Stage=stage,Data=data}));}
        private bool FundingReadbacksEnabledFor(string stage)=>FundingReadbacksEnabled&&(CurrentSafeFundingStageFilterForTests==null||CurrentSafeFundingStageFilterForTests(OwnerId,stage));
        private bool FundingReadbacksEnabled=>CurrentSafeFundingReadbackForTests!=null&&(CurrentSafeFundingOwnerFilterForTests==null||CurrentSafeFundingOwnerFilterForTests==OwnerId);
#endif
        private void GuardDefense(PlayableAiObservation o,bool coveragePending=false)
        {
            var threats=AiDefensePlanner.Threats(o,profile);
            bool uncovered=!coveragePending&&threats.Length>0&&!defense.CoversTechnologyThreats(o,profile,nativeProfile,armies,records);
#if UNITY_INCLUDE_TESTS
            bool critical;
            if(FundingReadbacksEnabledFor("GuardDefense"))
            {
                bool threatPresent=threats.Length>0;bool? technologyPending=threatPresent?(bool?)pendingItems.Any(Technology):null;
                bool? technologyHold=threatPresent&&!technologyPending.Value?(bool?)technologyFunding.HasHold(budget):null;
                bool technologyWork=technologyPending==true||technologyHold==true;bool? emergency=threatPresent&&technologyWork?(bool?)TechnologyEmergency(o):null;
                critical=!coveragePending&&threatPresent&&technologyWork&&emergency==true;
                FundingReadback("GuardDefense",new{ThreatIds=threats.Select(x=>x.Id).ToArray(),TechnologyPending=technologyPending.HasValue?technologyPending.Value.ToString():"NotReached-no-threat",TechnologyHold=technologyHold.HasValue?technologyHold.Value.ToString():technologyPending==true?"NotReached-short-circuit":"NotReached-no-threat",TechnologyEmergency=emergency.HasValue?emergency.Value.ToString():"NotReached-no-technology-work",Critical=critical});
            }
            else critical=!coveragePending&&threats.Length>0&&(pendingItems.Any(Technology)||technologyFunding.HasHold(budget))&&TechnologyEmergency(o);
#else
            bool critical=!coveragePending&&threats.Length>0&&(pendingItems.Any(Technology)||technologyFunding.HasHold(budget))&&TechnologyEmergency(o);
#endif
#if UNITY_INCLUDE_TESTS
            if(critical)technologyFunding.Suppress(o,budget,arbiter,true,"center-threat-uncovered");
#else
            if(critical)technologyFunding.Suppress(o,budget,arbiter);
#endif
            foreach(var item in pendingItems.Where(p=>uncovered&&Offensive(p)||critical&&Technology(p)||
                p.PolicyName==AiDefensePlanner.Policy&&p.Action.Kind==PlayableCommandKind.Attack&&!threats.Any(t=>t.Id==p.Action.TargetId)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Observed center threat changed tactical priority.");}
        }
        internal void GuardDefenseDelivery(PlayableDomain domain)=>GuardDefenseReadiness(domain);
        internal bool GuardDefenseReadiness(PlayableDomain domain)
        {
            if(!technologyFunding.HasHold(budget)&&!pendingItems.Any(p=>Offensive(p)||Technology(p)||p.PolicyName==AiDefensePlanner.Policy)||!domain.Authorizes(OwnerId)||domain.Outcome!=PlayableMatchOutcome.Playing)return true;
            authorityTick=domain.Tick;
            var current=PlayableAiObservation.From(domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId)));
            // Actual orders/receipts, live force and every threatened center are
            // checked before waiting. A new front or lost defense preempts now.
            if(defense.StructurallyCoversThreats(current,profile,nativeProfile,armies,records))
            {
                var routes=defense.TechnologyCoverageRoutes(current,profile,armies,records);
                if(routes.Count>0){
                    try{current=PlayableAiObservation.From(domain.PlayerSnapshotForDelivery(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId),routes));}
                    catch(ObservedPathPending){GuardDefense(current,true);return false;}
                }
            }
            GuardDefense(current);return true;
        }
        internal void InitializeOpeningProtection(PlayableDomain domain)
        {
            if(openingExecutor.Capture().StartedTick>=0)return;
            var view=PlayableAiObservation.From(domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId)));
            openingExecutor.Observe(view,null,null,nativeProfile);
        }
        private void BindUtilityReservations(PlayableAiObservation o,int preparationSite=0,bool considerProspectiveScout=false)
        {
            utilityReserved.Clear();
            foreach(var id in armies.Capture().Armies.Where(a=>a.Role==AiArmyRole.Scout&&a.Phase!=AiArmyPhase.Disbanded).SelectMany(a=>a.Members))utilityReserved.Add(id);
            foreach(var id in pendingItems.Where(p=>p.Scout).SelectMany(p=>p.Action.EntityIds))utilityReserved.Add(id);
            // A spare scout slot is not an assignment. Protect only the actor of a
            // current, admissible proposal; free combat Explorers must remain
            // available to army reinforcement between ordinary decisions.
            if(considerProspectiveScout&&pendingItems.Count<actionLimit&&!scout.HasPendingObligation&&!pendingItems.Any(p=>p.Scout))
            {
                var candidate=scoutPlanner.Plan(o,knowledge.Capture(),armies,profile,nativeProfile,ScoutBudget,preparationSite);
                if(candidate!=null&&candidate.EntityIds.All(id=>o.Entities.Any(u=>u.Id==id&&!u.Held&&!(u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Human))&&!pendingItems.Any(p=>p.Action.EntityIds.Contains(id))&&(!expansion.ClaimsActor(id)||expansion.AllowsScoutReuse(id))))
                    foreach(var id in candidate.EntityIds)utilityReserved.Add(id);
            }
            armyPlanner.UtilityMembers=flankPlanner.UtilityMembers=utilityReserved;
        }
        private int[] FlankReservations(PlayableAiObservation o)
        {
            int minimum=(int)nativeProfile.Value("armies.minimumUnits");
            if(nativeConfig.Difficulty==AiDifficulty.Recruit||flankPlanner.Active||openingExecutor.Capture().Phase!=PlayableAiOpeningPhase.Complete||
                o.Entities.Count(u=>u.Owner==o.Owner&&u.Health>0&&u.Id!=openingExecutor.ProtectedUnit&&!utilityReserved.Contains(u.Id)&&roster.For(u.Kind).lineWeight>0)<minimum*2)return Array.Empty<int>();
            var members=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.Id!=openingExecutor.ProtectedUnit&&!utilityReserved.Contains(u.Id)&&
                !u.Held&&!(u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Human)&&roster.For(u.Kind).lineWeight>0&&armies.ArmyFor(u.Id)==0).OrderBy(u=>u.Id).Take(minimum).Select(u=>u.Id).ToArray();
            // An incomplete second force cannot be admitted. Let these units
            // reinforce the existing army instead of reserving them indefinitely.
            return members.Length==minimum?members:Array.Empty<int>();
        }
        internal void Review(PlayableSnapshot snapshot,long humanSequence)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.AiReview);
            authorityTick=snapshot.Tick;
            if(snapshot.OwnerEliminated||snapshot.Generation!=Generation||snapshot.Status==RuntimeStatus.Stopped||snapshot.Status==RuntimeStatus.Failed||snapshot.Outcome!=PlayableMatchOutcome.Playing){while(pending!=null)Cancel(PlayableAiDeliveryStatus.Stopped,"Match ended.");armies.DisbandAll();armyPlanner.Stop();flankPlanner.Stop();defense.Stop();Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);return;}
            if(snapshot.Paused)return;
            var current=PlayableAiObservation.From(snapshot);
            BindCombatMicro(current);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            using var qaRouteAudit=QaAiRouteAudit.Begin(current,profile);
#endif
            BindUtilityReservations(current);
            openingExecutor.SpendableCredits=budget.Available;
            openingExecutor.CombatAvailable=id=>!utilityReserved.Contains(id)&&!expansion.ClaimsActor(id)&&!armies.IsScoutMember(id);
            budget.ObserveBank(current.Credits);
            doctrine.SpendableCredits=budget.Available;
            doctrine.Observe(current,profile,nativeProfile,openingExecutor,nativeConfig.Difficulty);
            openingExecutor.AggressivePersonality=doctrine.Personality==AiPersonality.Aggressive;
            openingExecutor.Observe(current,null,null,nativeProfile);
            armyPlanner.Personality=flankPlanner.Personality=doctrine.Personality;
            armyPlanner.HarassAllowed=flankPlanner.HarassAllowed=doctrine.HarassAllowed(nativeConfig.Difficulty);
            armyPlanner.SplitLargeArmies=flankPlanner.SplitLargeArmies=nativeConfig.Difficulty!=AiDifficulty.Recruit;
            scoutPlanner.RevisitScale=doctrine.Personality==AiPersonality.Harassing?nativeProfile.Value("openingsMidgame.personality.harassingScoutScale"):1;
            armyPlanner.OpeningMinimum=openingExecutor.Capture().Phase==PlayableAiOpeningPhase.Active?openingExecutor.RequiredForce:0;
            armyPlanner.OpeningMembers=openingExecutor.InitialMembers(current,nativeProfile);
            armyPlanner.ProtectedUnit=openingExecutor.ProtectedUnit;
            scoutPlanner.ProtectedUnit=openingExecutor.ProtectedUnit;
            defense.ProtectedUnit=openingExecutor.ProtectedUnit;
            expansion.AllowOutposts=openingExecutor.AllowsOutposts;
            IReadOnlyList<int> newCoverage;
            using(PerformanceProfile.Measure(ProfileStage.Knowledge))newCoverage=knowledge.Observe(current,nativeProfile);
            var reviewSection=PerformanceProfile.Measure(ProfileStage.AiReviewGuard);
            expansion.Knowledge=knowledge.Capture();
            armyPlanner.Knowledge=knowledge.Capture();
            flankPlanner.Knowledge=knowledge.Capture();
            if(newCoverage.Any(id=>current.PublicScoutObjectives.Any(p=>p.SiteId==id)))lastScoutTick=current.Tick;
            GuardDefense(current);
            reviewSection.Dispose();reviewSection=PerformanceProfile.Measure(ProfileStage.AiReviewFinance);
            budget.ObserveBank(current.Credits);
            expansionFinancial.Reconcile(current,profile,nativeProfile,openingExecutor,budget,arbiter,expansion);
            int preparationSite=expansionFinancial.PreparationSite(current,profile,nativeProfile,budget,arbiter,expansion,openingExecutor.AllowsExpansion);
            if(preparationSite==0)
            {
                var heldExpansion=expansion.Capture();
                if(heldExpansion.SiteId>0&&scoutPlanner.IsPreparationActor(armies,heldExpansion.SiteId,heldExpansion.ActorId))preparationSite=heldExpansion.SiteId;
            }
            reviewSection.Dispose();reviewSection=PerformanceProfile.Measure(ProfileStage.AiReviewArmies);
            armies.Observe(current);
            scoutPlanner.Observe(current,knowledge.Capture(),armies,nativeProfile,ScoutBudget,preparationSite);
            foreach(var item in pendingItems.Where(x=>x.Scout&&x.Action.EntityIds.Any(id=>armies.ArmyFor(id)==0)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Scout assignment released.");}
            BindUtilityReservations(current,preparationSite,current.Tick-lastDecisionTick>=DecisionIntervalTicks);
            armyPlanner.Observe(current,armies,nativeProfile,records,opening,openingExecutor.AllowsPressure,current.Tick-lastDecisionTick>=DecisionIntervalTicks);
            flankPlanner.ProtectedUnit=openingExecutor.ProtectedUnit;flankPlanner.OtherTargetId=armyPlanner.Capture().TargetId;
            flankPlanner.Observe(current,armies,nativeProfile,records,opening,openingExecutor.AllowsPressure,current.Tick-lastDecisionTick>=DecisionIntervalTicks);
            bool newEmergency=defense.Observe(current,armies,profile);
            // An applied, currently certified and sufficient defense already owns
            // this threat. Spare production must remain available for pressure.
            bool defenseCovered=defense.Active&&defense.CoversTechnologyThreats(current,profile,nativeProfile,armies,records);
            var flankReserve=FlankReservations(current);
            var unavailableTacticalActors=pendingItems.SelectMany(x=>x.Action.EntityIds).Concat(current.Entities.Where(u=>expansion.ClaimsActor(u.Id)).Select(u=>u.Id)).Concat(flankReserve).Concat(utilityReserved).Concat(openingExecutor.ProtectedUnit>0?new[]{openingExecutor.ProtectedUnit}:Array.Empty<int>()).ToArray();
            if(defense.Active)AiTacticalExecutor.Observe(current,armies,defense.ArmyId,profile,
                defenseCovered?unavailableTacticalActors.Concat(current.Entities.Where(u=>u.Owner==current.Owner&&armies.ArmyFor(u.Id)==0).Select(u=>u.Id)):unavailableTacticalActors,AiDefensePlanner.Policy,records,nativeProfile,knowledge.Capture());
            else if(armyPlanner.Active)AiTacticalExecutor.Observe(current,armies,armyPlanner.Capture().ArmyId,profile,
                unavailableTacticalActors.Concat(current.Entities.Where(u=>u.Held||u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Human).Select(u=>u.Id)),AiArmyPlanner.Policy,records,nativeProfile,knowledge.Capture());
            foreach(var item in pendingItems.Where(x=>x.PolicyName==AiDefensePlanner.Policy&&!defense.Active).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Defense army released.");}
            if(!defense.Active&&flankPlanner.Active)AiTacticalExecutor.Observe(current,armies,flankPlanner.Capture().ArmyId,profile,unavailableTacticalActors,AiArmyPlanner.FlankPolicy,records,nativeProfile,knowledge.Capture());
            foreach(var item in pendingItems.Where(x=>x.PolicyName==AiArmyPlanner.FlankPolicy&&!flankPlanner.Active).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,"Flank mission released.");}
            foreach(var item in pendingItems.Where(x=>x.PolicyName==AiArmyPlanner.Policy&&!armyPlanner.Active).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,"Army mission released.");}
            reviewSection.Dispose();reviewSection=PerformanceProfile.Measure(ProfileStage.AiReviewFunding);
            budget.Expire(snapshot.Tick,entry=>BudgetReservationValid(entry,current));technologyFunding.Reconcile(current,budget,arbiter);
            launches.Observe(current,budget,profile);
            foreach(var item in pendingItems.Where(x=>x.PolicyName=="production"&&x.Intent.ReservationId!=null&&!launches.Contains(x.Intent.ReservationId)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,"Factory startup commitment expired or target lost.");}
            reviewSection.Dispose();reviewSection=PerformanceProfile.Measure(ProfileStage.AiReviewExpansion);
#if UNITY_INCLUDE_TESTS
            expansion.Observe(current,profile,nativeProfile,snapshot.ExactCredits);
#else
            expansion.Observe(current,profile,nativeProfile);
#endif
            expansion.ReconcileFunding(budget,profile,authorityTick,pendingItems.Select(x=>x.Intent));
            foreach(var item in pendingItems.Where(x=>x.PolicyName=="expansion"&&!expansion.ClaimsSite(x.Action.SiteId)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,"Expansion claim released.");}
            infrastructure.Observe(current,profile,budget);
            foreach(var item in pendingItems.Where(x=>x.PolicyName=="infrastructure"&&(x.Action.Kind==PlayableCommandKind.SellBuilding||x.Action.Kind==PlayableCommandKind.BuildAt)&&!infrastructure.ClaimsSlot(x.Action.SiteId,x.Action.SlotId)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,"Conversion released.");}
            reviewSection.Dispose();reviewSection=PerformanceProfile.Measure(ProfileStage.AiReviewOpening);
            openingExecutor.Observe(current,knowledge.Capture(),armyPlanner.Deployment(current,armies),nativeProfile);
            demand.Observe(current,nativeProfile);composition.Observe(current,profile,nativeProfile);
            var actualThreats=AiDefensePlanner.Threats(current,profile);bool threatened=actualThreats.Length>0;
            var earlyDefense=newEmergency?defense.Propose(current,profile,nativeProfile,armies):null;
            // A non-admissible threat must not open an early macro/scout decision.
            // Only actual survival admission can override ordinary decision cadence.
            reviewSection.Dispose();
            if(snapshot.Tick-lastDecisionTick<DecisionIntervalTicks&&earlyDefense==null)return;
            using var decisionMeasured=PerformanceProfile.Measure(ProfileStage.AiReviewDecision);
            lastDecisionTick=snapshot.Tick;
#if UNITY_INCLUDE_TESTS
            bool captureExpansionDecisionReadback=CurrentExpansionDecisionReadbackForTests!=null&&(CurrentExpansionDecisionOwnerFilterForTests==null||CurrentExpansionDecisionOwnerFilterForTests==OwnerId);
            var beforeFinanceDetach=captureExpansionDecisionReadback?budget.Capture():null;
#endif
            var previousFinancial=expansionFinancial.Detach(budget);
            var observation=current;
            if(observation.OwnerId!=OwnerId)throw new InvalidOperationException("Owner loop received a foreign projection.");
#if UNITY_INCLUDE_TESTS
            // Diagnostic-only capture of the actual pre-proposal observation; never feeds planner decisions.
            if(CurrentOwnerDecisionReadbackForTests!=null&&(CurrentOwnerDecisionOwnerFilterForTests==null||CurrentOwnerDecisionOwnerFilterForTests==OwnerId))
                CurrentOwnerDecisionReadbackForTests(OwnerId,observation.Tick,observation,armies.Capture(),pendingItems.Select(p=>p.Action).ToArray());
#endif
            budget.ObserveBank(observation.Credits);
            budget.Expire(snapshot.Tick,entry=>BudgetReservationValid(entry,observation));technologyFunding.Reconcile(observation,budget,arbiter);
            var strategy=midgame.Review(observation,opening.WithPhase(openingExecutor.Capture().Phase),mission.Mission,nativeDeployment:armyPlanner.Deployment(observation,armies),personality:doctrine.Personality);
            combatMicro.Observe(observation,profile,nativeProfile,nativeConfig.Difficulty,armies);
            BindCombatMicro(observation);
            var proposals=new List<AiIntent>();
            bool Available(string name)=>!pendingItems.Any(p=>p.PolicyName==name);
            AiIntent Propose(string name,PlayableAiAction candidate,Action commit,Action<PlayableAiTraceRecord> feedback,int priority=0,int? requireFullyFundableAfterProtection=null)
            {
                bool repairHandoff=candidate!=null&&combatMicro.CanYieldPendingRepair(observation,profile,nativeProfile,nativeConfig.Difficulty,armies,name,candidate)&&
                    observation.RouteProofs.Any(r=>r.UnitId==candidate.EntityIds[0]&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==candidate.ParentId&&r.Goal.Equals(candidate.Target)&&AiTacticalExecutor.WithdrawalRoute(observation,r,profile));
                if(repairHandoff)priority=2; // Existing survival band; no extra attention, actions or reaction time.
                if(candidate!=null&&name!=AiCombatMicroPolicy.Policy&&MicroReserved(candidate)&&priority<2)return null;
                if(candidate==null||!Available(name))
                {
#if UNITY_INCLUDE_TESTS
                    if(name=="expansion"&&ProposalAdmissionReadbackForTests!=null&&(ProposalAdmissionOwnerFilterForTests==null||ProposalAdmissionOwnerFilterForTests==OwnerId))
                        ProposalAdmissionReadbackForTests(new PlayableAiProposalAdmissionReadback(OwnerId,observation.Tick,name,candidate,candidate==null?"planner-result":"policy-cooldown",candidate==null?"actual planner returned null":"policy already pending"));
#endif
                    return null;
                }
                if(name!="expansion"&&candidate.EntityIds.Any(id=>expansion.ClaimsActor(id)&&!(name=="scout"&&candidate.Kind==PlayableCommandKind.Move&&expansion.AllowsScoutReuse(id)))&&priority<2)return null;
                if(candidate.PlayerId!=OwnerId||candidate.Generation!=Generation||candidate.Seed!=observation.Seed||candidate.SourceIdentity!=opening.SourceIdentity||candidate.ProfileId!=observation.ProfileId||candidate.ProfileRevision!=observation.ProfileRevision||candidate.SnapshotSequence!=observation.SnapshotSequence)
                    throw new InvalidOperationException("AI policy returned an action outside its owner observation.");
                var intent=Proposal(name,candidate,observation,priority,repairHandoff);
                if(intent==null)return null;
                if(requireFullyFundableAfterProtection.HasValue&&!AiExpansionFinancial.FullyFundable(budget,requireFullyFundableAfterProtection.Value,intent))
                {
#if UNITY_INCLUDE_TESTS
                    if(name=="expansion"&&ProposalAdmissionReadbackForTests!=null&&(ProposalAdmissionOwnerFilterForTests==null||ProposalAdmissionOwnerFilterForTests==OwnerId))
                        ProposalAdmissionReadbackForTests(new PlayableAiProposalAdmissionReadback(OwnerId,observation.Tick,name,candidate,"fully-fundable-after-protection","actual ledger/protection funding admission failed"));
#endif
                    return null;
                }
                intent.Commit=commit;intent.Feedback=feedback;proposals.Add(intent);return intent;
            }
            // TryPlan is a legacy stateful API. Each candidate runs on a transaction fork;
            // only selected forks become live policies. Unselected RNG/pending/mission changes disappear.
            var da=earlyDefense??defense.Propose(observation,profile,nativeProfile,armies);
            var defenseAssembly=earlyDefense==null&&AiDefensePlanner.LastHeadquartersAttackers(observation).Length==0?defense.ProposeAssembly(observation,profile,armies,records,nativeProfile,knowledge.Capture()):null;
            var defenseAction=defenseAssembly??da;
            Propose(AiDefensePlanner.Policy,defenseAction,()=>
            {
                if(defenseAssembly!=null)defense.CommitAssembly(observation,defenseAssembly,armies,profile,records);
                else {defense.Commit(observation,da,armies,profile);armyPlanner.ReconcileRegistry(armies,authorityTick,"defense preempted offensive ownership");flankPlanner.ReconcileRegistry(armies,authorityTick,"defense preempted flank ownership");}
            },defense.ObserveReceipt,2);
            if(!threatened||defenseCovered)
            {
                armyPlanner.OtherTargetId=flankPlanner.Active?flankPlanner.Capture().TargetId:0;
                var armyAction=armyPlanner.Active||openingExecutor.AllowsPressure?armyPlanner.ProposeWithRecords(observation,opening.WithPhase(openingExecutor.Capture().Phase),profile,nativeProfile,armies,records):armyPlanner.ProposePositioning(observation,profile,nativeProfile,armies);
                Propose(AiArmyPlanner.Policy,armyAction,()=>armyPlanner.Commit(observation,armyAction,armies),armyPlanner.ObserveReceipt,armyPlanner.ReinforcementPriority(observation,armies,armyAction));
                if(armyPlanner.Active&&nativeConfig.Difficulty!=AiDifficulty.Recruit&&openingExecutor.Capture().Phase==PlayableAiOpeningPhase.Complete&&current.Entities.Count(u=>u.Owner==current.Owner&&u.Health>0&&!utilityReserved.Contains(u.Id)&&roster.For(u.Kind).lineWeight>0)>=nativeProfile.Value("armies.minimumUnits")*2)
                {
                    flankPlanner.OtherTargetId=armyPlanner.Capture().TargetId;
                    var flankAction=flankPlanner.ProposeWithRecords(observation,opening.WithPhase(PlayableAiOpeningPhase.Complete),profile,nativeProfile,armies,records);
                    Propose(AiArmyPlanner.FlankPolicy,flankAction,()=>flankPlanner.Commit(observation,flankAction,armies),flankPlanner.ObserveReceipt,flankPlanner.ReinforcementPriority(observation,armies,flankAction));
                }
                // Generic composition now owns artillery production; Army/TacticalExecutor retain combat ownership.
            }
            bool usefulScout=scoutPlanner.UsefulProductionDemand(observation,knowledge.Capture(),nativeProfile,ScoutBudget);
            AiEconomyPlan normalEconomy=null;
            AiEconomyPlan ComposeEconomy()
            {
                compositionAssessment=composition.Assess(observation,profile,nativeProfile,budget.Capture(),ScoutBudget,
                    observation.Buildings.Where(b=>launches.ContainsProducer(b.Id)).Select(b=>b.Id),usefulScoutDemand:usefulScout);
                normalEconomy=new AiEconomyPlanner(roster).Plan(observation,profile,demand,nativeProfile,budget.Available,
                    productionKind:doctrine.Ready?doctrine.GroundProductionKind(observation,nativeProfile):compositionAssessment.Candidates.LastOrDefault()?.Action.UnitKind??PlayableEntityKind.Tank);
                return new AiEconomyPlan(normalEconomy.Candidates.Where(c=>c.Policy!="production").Concat(compositionAssessment.Candidates).Concat(
                    compositionAssessment.Candidates.Count==0?normalEconomy.Candidates.Where(c=>c.Policy=="production").Select(c=>new AiEconomyCandidate(c.Policy,c.Action,c.Reason??compositionAssessment.Reason)):Array.Empty<AiEconomyCandidate>()),
                    normalEconomy.ExpansionSiteIds,normalEconomy.ExpansionReason,normalEconomy.Capacity);
            }
            EconomyPlan=ComposeEconomy();
            var armyResearchPolicy=AiArmyResearchPolicy.Observe(observation,profile,nativeConfig.Difficulty,composition.Capture().Phase,arbiter);
            bool technologyEmergency=TechnologyEmergency(observation);
            TechnologyPlan=technology.Plan(observation,profile,nativeProfile,budget.Capture(),technologyEmergency,roster,technologyFunding.Preferred(arbiter),armyResearchPolicy,doctrine);
#if UNITY_INCLUDE_TESTS
            if(FundingReadbacksEnabledFor("TechnologyPlanBeforeFunding"))FundingReadback("TechnologyPlanBeforeFunding",new{Emergency=technologyEmergency,Reason=TechnologyPlan.Reason,Action=TechnologyPlan.Action==null?null:new{TechnologyPlan.Action.Kind,TechnologyPlan.Action.ResearchKind,TechnologyPlan.Action.SiteId,TechnologyPlan.Action.SlotId},Goal=TechnologyPlan.FundingGoal==null?null:new{TechnologyPlan.FundingGoal.Research,TechnologyPlan.FundingGoal.Utility},Branches=TechnologyPlan.Branches.Select(b=>new{Research=b.Descriptor.Kind,Progress=b.Progress.ToString(),b.Utility,b.MissingCost,b.WaitingSeconds,b.AffectedValue}).ToArray()});
#endif
            bool missingScout=usefulScout&&!observation.Entities.Any(u=>u.Owner==observation.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0)&&!observation.Buildings.Where(b=>b.Owner==observation.Owner&&b.PrivateState!=null).SelectMany(b=>b.PrivateState.Orders).Any(q=>q.Kind==PlayableEntityKind.Explorer)&&!budget.Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer);
            bool urgentRepair=infrastructure.Plan(observation,profile,nativeProfile,EconomyPlan.Capacity).Any(c=>c.Legal&&c.Priority>0&&c.Action.Kind==PlayableCommandKind.StartBuildingRepair);
            // Assess scaling with our own held cash restored to the available band: a
            // funding goal cannot manufacture its own "startup unavailable" blocker.
            var scaling=demand.Assess(observation,profile,nativeProfile,budget.Available+budget.Capture().Reserved.Where(AiTechnologyFunding.IsFunding).Sum(e=>(long)e.Amount),roster,
                doctrine.Ready?doctrine.GroundProductionKind(observation,nativeProfile):compositionAssessment.Candidates.LastOrDefault()?.Action.UnitKind??PlayableEntityKind.Tank);
            bool essentialScale=scaling.CanScale&&normalEconomy.Candidates.Any(c=>c.Policy=="economy"&&c.Action.Kind==PlayableCommandKind.BuildAt&&c.Action.BuildingKind==PlayableBuildingKind.Factory&&AiEconomyAdmission.Reject(observation,profile,c.Action)==null&&!expansion.ClaimsSite(c.Action.SiteId)&&!infrastructure.ClaimsSlot(c.Action.SiteId,c.Action.SlotId));
            bool homeEconomyReady=openingExecutor.HomeEconomyReady(observation,nativeProfile);
            bool initialGrowthCommitmentPending=openingExecutor.InitialGrowthCommitmentPending(observation,nativeProfile);
            int scoutProtection=missingScout?PlayableUnitRules.Cost(profile,PlayableEntityKind.Explorer):0;
            bool financialAllowed=!TechnologyPlan.PriorityResearch&&!threatened&&!urgentRepair&&openingExecutor.AllowsExpansion;
            int firstIncomeCost=expansionFinancial.BootstrapActive(arbiter)?
                openingExecutor.HomeEconomyQuote(observation,profile,nativeProfile,normalEconomy.Candidates):
                expansionFinancial.PriorityCost(observation,profile,nativeProfile,arbiter,expansion,previousFinancial,financialAllowed);
            var investment=AiEconomicInvestmentPlanner.Plan(observation,profile,nativeProfile,scaling,homeEconomyReady,threatened||urgentRepair,
                AiEconomicInvestmentPlanner.Demand(budget),initialGrowthCommitmentPending,scoutProtection,budget.Available,doctrine.Ready?doctrine.GroundProductionKind(observation,nativeProfile):PlayableEntityKind.Tank);
            bool missingUsefulScience=TechnologyPlan.FundingGoal!=null&&!initialGrowthCommitmentPending&&
                !observation.Buildings.Any(b=>b.Owner==observation.Owner&&b.Health>0&&b.Kind==PlayableBuildingKind.ScientificCenter);
            // The first safe income capture supplies growth without consuming home
            // slots. Useful missing science can precede another discretionary factory.
            if(doctrine.ProducerRequests(observation,profile).Any(c=>c.Legal||c.Reason=="insufficient credits")||openingExecutor.HasContinuationCommitment(nativeProfile)||!openingExecutor.Capture().InitialForceFulfilled||openingExecutor.Air&& !observation.Buildings.Any(b=>b.Owner==observation.Owner&&b.Kind==PlayableBuildingKind.Factory)||firstIncomeCost>0||missingUsefulScience&&investment?.Candidate.Action.BuildingKind==PlayableBuildingKind.Factory)investment=null;
            AiEconomicInvestmentPlanner.RememberDemand(observation,profile,budget,investment);
            bool preserveScience=technologyFunding.HasAcceptedScience(budget,arbiter);
            bool priorityResearch=TechnologyPlan.PriorityResearch;
            bool deferTechnology=investment!=null&&!preserveScience&&!priorityResearch;
            int protectedFunds=0;
#if UNITY_INCLUDE_TESTS
            int productiveCycleReadback=0,initialForceProtectionReadback=0;
#endif
            // A useful idle-line cycle must remain payable outside discretionary savings.
            // Its actual composition/admission and current gameplay price select the band.
            var held=budget.Capture();
            if(held.Reserved.Any(launches.IsStartup))
            {
                // Pure preflight only: own discretionary savings cannot hide a useful
                // old-line cycle. All other holds, safety and unpaid invoices stay counted.
                var withoutOwnSavings=new AiBudgetState(held.OwnerId,held.Generation,held.Liquid,held.SafetyReserve,held.PaidTotal,
                    held.Reserved.Where(e=>!AiTechnologyFunding.IsFunding(e)),held.Unpaid,held.Paid,held.ReservationLifetimeTicks,held.AdmittedDecision,held.AdmittedAction,held.RepairPaidTotal);
                var usefulCycle=composition.Assess(observation,profile,nativeProfile,withoutOwnSavings,ScoutBudget,
                    observation.Buildings.Where(b=>launches.ContainsProducer(b.Id)).Select(b=>b.Id),usefulScoutDemand:usefulScout);
                int productiveCycle=usefulCycle.Candidates.Where(c=>c.Legal&&c.Policy=="production"&&PlayableUnitRules.ProductionKind(c.Action.Kind).HasValue).Select(c=>PlayableUnitRules.Cost(profile,c.Action.UnitKind)).DefaultIfEmpty(0).Min();
#if UNITY_INCLUDE_TESTS
                productiveCycleReadback=productiveCycle;
#endif
                protectedFunds=Math.Max(protectedFunds,productiveCycle);
            }
            int initialForceProtection=openingExecutor.InitialForceFundingProtection(observation,profile,nativeProfile,held);
            protectedFunds=Math.Max(protectedFunds,checked(initialForceProtection+openingExecutor.HomeEconomyFundingProtection(observation,profile,nativeProfile,held)));
            protectedFunds=Math.Max(protectedFunds,doctrine.ProducerRequests(observation,profile).Where(c=>c.Legal||c.Reason=="insufficient credits").Select(c=>TerritoryRules.Cost(profile,c.Action.BuildingKind)).DefaultIfEmpty(0).Min());
#if UNITY_INCLUDE_TESTS
            initialForceProtectionReadback=initialForceProtection;
#endif
#if UNITY_INCLUDE_TESTS
            long startupProtection=scaling.CanScale||scaling.ScalingReason=="factory startup fund unavailable"?scaling.StartupFund:0L;
#endif
            // Position development competes with discretionary growth quotes while
            // retaining opening, scout and existing paid/startup ledger protections.
            int positionProtectedFunds=checked(protectedFunds+scoutProtection);
            if(!priorityResearch&&(scaling.CanScale||scaling.ScalingReason=="factory startup fund unavailable"))protectedFunds=checked(protectedFunds+(int)scaling.StartupFund);
            if(investment!=null&&!preserveScience&&!priorityResearch)protectedFunds=Math.Max(protectedFunds,investment.Credits);
            protectedFunds=checked(protectedFunds+scoutProtection);
            // A local growth proposal is not exclusive ownership of the entire
            // development budget. Safe capture keeps its ordinary ledger admission.
#if UNITY_INCLUDE_TESTS
            if(FundingReadbacksEnabledFor("ExpansionFinancialAdmissionInputs"))FundingReadback("ExpansionFinancialAdmissionInputs",new{FinancialAllowed=financialAllowed,Threatened=threatened,UrgentRepair=urgentRepair,AllowsExpansion=openingExecutor.AllowsExpansion,
                ProtectedFunds=protectedFunds,ProtectionSources=new{Scout=scoutProtection,ProductiveCycle=productiveCycleReadback,InitialForce=initialForceProtectionReadback,Startup=startupProtection,Investment=investment?.Credits??0},FirstIncomeCost=firstIncomeCost,
                PreviousFinancial=previousFinancial,LedgerBeforeTechnologyAllocation=budget.Capture(),DynamicSites=observation.Sites.ToArray(),DiscoveredObjectives=observation.PublicScoutObjectives.ToArray(),
                ActorClaims=new{Army=armies.Capture().Armies.SelectMany(a=>a.Members).Distinct().OrderBy(id=>id).ToArray(),Pending=pendingItems.SelectMany(i=>i.Action.EntityIds).Distinct().OrderBy(id=>id).ToArray()},
                Explorers=observation.Entities.Where(e=>e.Owner==observation.Owner&&e.Kind==PlayableEntityKind.Explorer).Select(e=>new{e.Id,e.Health,e.Moving,e.Position,e.CurrentOrder,e.NavigationOutcome}).ToArray()});
#endif
            var positionChoices=AiPositionDevelopmentPlanner.Plan(observation,profile,nativeProfile,opening.WithPhase(openingExecutor.Capture().Phase),knowledge.Capture(),true,openingExecutor.Air);
            var positionSaving=AiPositionDevelopmentFunding.Quote(observation,profile,nativeProfile,arbiter,
                positionChoices,positionProtectedFunds,!urgentRepair&&!openingExecutor.MissingEssentialEconomy(observation,nativeProfile)&&
                !pendingItems.Any(p=>p.PolicyName=="economy")&&!priorityResearch&&firstIncomeCost==0);
            int positionPrice=positionSaving?.Credits??0;
            // Reconcile only unaccepted discretionary research through its existing
            // allocator; accepted science, invoices and startup reservations stay owned.
            technologyFunding.Observe(observation,profile,nativeProfile,budget,arbiter,TechnologyPlan,CanFundTechnology(observation,urgentRepair,essentialScale&&!priorityResearch&&!missingUsefulScience)&&!deferTechnology,protectedFunds,Math.Max(firstIncomeCost,positionPrice));
            if(positionSaving!=null)protectedFunds=Math.Max(protectedFunds,checked(positionProtectedFunds+positionPrice));
#if UNITY_INCLUDE_TESTS
            if(FundingReadbacksEnabledFor("TechnologyAllocation"))FundingReadback("TechnologyAllocation",new{ProtectedFunds=protectedFunds,MissingScout=missingScout,UrgentRepair=urgentRepair,EssentialScale=essentialScale,ScalingReason=scaling.ScalingReason,Startup=scaling.StartupFund,Ledger=budget.Capture().Reserved.Where(e=>AiTechnologyFunding.IsFunding(e)).Select(e=>new{e.Id,e.Amount,e.CreatedTick,e.ExpiresTick}).ToArray()});
#endif
#if UNITY_INCLUDE_TESTS
            var afterTechnologyAllocation=captureExpansionDecisionReadback?budget.Capture():null;
#endif
            // An entirely protected first-wave bank cannot begin useful savings.
            // Do not age a new zero goal into a permanent expiry before it can be
            // funded. Existing goals keep their original clocks and tombstones.
            bool incomeFundingAllowed=financialAllowed&&(!initialGrowthCommitmentPending||budget.Available>protectedFunds);
            expansionFinancial.Observe(observation,profile,nativeProfile,openingExecutor,budget,arbiter,expansion,previousFinancial,protectedFunds,incomeFundingAllowed);
            int financialSite=expansionFinancial.ActiveSite(arbiter);
            bool financialReady=expansionFinancial.Ready(profile,budget,arbiter,observation.Tick);
            int preparationScoutSite=financialAllowed&&financialSite!=0?
                expansionFinancial.PreparationSite(observation,profile,nativeProfile,budget,arbiter,expansion,openingExecutor.AllowsExpansion):0;
            if(preparationScoutSite==0)
            {
                var heldExpansion=expansion.Capture();
                if(heldExpansion.SiteId>0&&scoutPlanner.IsPreparationActor(armies,heldExpansion.SiteId,heldExpansion.ActorId))preparationScoutSite=heldExpansion.SiteId;
            }
#if UNITY_INCLUDE_TESTS
            var afterFinancialAllocation=captureExpansionDecisionReadback?budget.Capture():null;
            if(FundingReadbacksEnabledFor("ExpansionFinancialAllocation"))FundingReadback("ExpansionFinancialAllocation",new{FinancialAllowed=financialAllowed,FirstIncomeCost=firstIncomeCost,ProtectedFunds=protectedFunds,
                PreviousFinancial=previousFinancial,BeforeFinanceDetach=beforeFinanceDetach,AfterTechnologyAllocation=afterTechnologyAllocation,AfterFinancialAllocation=budget.Capture()});
#endif
            EconomyPlan=ComposeEconomy(); // Real ledger after allocation, never the preflight band.
            TechnologyPlan=technology.Plan(observation,profile,nativeProfile,budget.Capture(),TechnologyEmergency(observation),roster,technologyFunding.Preferred(arbiter),armyResearchPolicy,doctrine);
#if UNITY_INCLUDE_TESTS
            if(FundingReadbacksEnabledFor("TechnologyPlanAfterFunding"))FundingReadback("TechnologyPlanAfterFunding",new{Reason=TechnologyPlan.Reason,Action=TechnologyPlan.Action==null?null:new{TechnologyPlan.Action.Kind,TechnologyPlan.Action.ResearchKind},Goal=TechnologyPlan.FundingGoal==null?null:new{TechnologyPlan.FundingGoal.Research,TechnologyPlan.FundingGoal.Utility},Branches=TechnologyPlan.Branches.Select(b=>new{Research=b.Descriptor.Kind,Progress=b.Progress.ToString(),b.Utility,b.MissingCost,b.WaitingSeconds,b.AffectedValue}).ToArray()});
#endif
            if(deferTechnology)TechnologyPlan=new AiTechnologyPlan(TechnologyPlan.Branches.ToArray(),null,null,investment.Reason);
            EconomyPlan=new AiEconomyPlan(EconomyPlan.Candidates.Concat(new AiEconomyPlanner(roster).InfrastructureRequests(observation,profile,TechnologyPlan.Infrastructure)),EconomyPlan.ExpansionSiteIds,EconomyPlan.ExpansionReason,EconomyPlan.Capacity);
            EconomyPlan=new AiEconomyPlan(openingExecutor.MacroRequests(observation,profile,nativeProfile,EconomyPlan.Candidates,initialGrowthCommitmentPending,doctrine.ProducerRequests(observation,profile)),EconomyPlan.ExpansionSiteIds,EconomyPlan.ExpansionReason,EconomyPlan.Capacity);
            if(investment!=null)
            {
                var investCandidate=investment.Candidate;
                EconomyPlan=new AiEconomyPlan(new[]{investCandidate}.Concat(EconomyPlan.Candidates.Where(c=>AiEconomyAdmission.IntentId(c.Policy,c.Action)!=AiEconomyAdmission.IntentId(investCandidate.Policy,investCandidate.Action)).Select(c=>
                    c.Policy=="production"&&!investment.AllowsProduction(budget.Available,PlayableUnitRules.Cost(profile,c.Action.UnitKind),launches.ReservationFor(c.Action)!=null,
                        missingScout&&c.Action.UnitKind==PlayableEntityKind.Explorer)?
                    new AiEconomyCandidate(c.Policy,c.Action,c.Reason??investment.Reason,c.Priority):c)),EconomyPlan.ExpansionSiteIds,EconomyPlan.ExpansionReason,EconomyPlan.Capacity);
            }
            bool FreshActor(int id)
            {
#if UNITY_INCLUDE_TESTS
                bool tacticalUnclaimed=armies.ArmyFor(id)==0;
                if(!AiExpansionPlanner.Guard(OwnerId,observation.Tick,0,id,"actor-tactical-owner",tacticalUnclaimed))return false;
                bool noPendingAction=!pendingItems.Any(i=>i.Action.EntityIds.Contains(id));
                return AiExpansionPlanner.Guard(OwnerId,observation.Tick,0,id,"actor-pending-action",noPendingAction);
#else
                return armies.ArmyFor(id)==0&&!pendingItems.Any(i=>i.Action.EntityIds.Contains(id));
#endif
            }
            bool FreshFinancialActor(int id)=>FreshActor(id)||
                preparationScoutSite>0&&scoutPlanner.IsPreparationActor(armies,preparationScoutSite,id)&&!pendingItems.Any(i=>i.Action.EntityIds.Contains(id));
            bool AdmissibleNewSite(int id)=>(teamAssignment==null||!teamAssignment.DeniedSites.Contains(id))&&!expansionFinancial.IsExpiredFingerprint(observation.Sites.FirstOrDefault(s=>s.Site.Id==id),arbiter);
            expansion.PreferRemote=expansionFinancial.RemotePreference(observation,nativeProfile,arbiter);
            expansion.LocalSiteId=expansionFinancial.LocalSite(observation,profile,nativeProfile,arbiter,expansion);
            PlayableAiAction expansionAction=null;
            if(!threatened&&openingExecutor.AllowsExpansion)
            {
                if(expansion.Capture().SiteId!=0)
                {
                    // A live owner commitment continues through the ordinary planner even
                    // when a separate financial memo is no longer ready.
                    var continuing=expansion.Fork();
                    expansionAction=continuing.TryPlan(observation,profile,nativeProfile,0,FreshActor,AdmissibleNewSite);
                    Propose("expansion",expansionAction,()=>expansion=continuing,continuing.ObserveReceipt,1);
                }
                else if(!expansionFinancial.BootstrapActive(arbiter))
                {
                    bool financeIntentAdded=false;
                    if(financialSite!=0&&expansionFinancial.Ready(profile,budget,arbiter,observation.Tick))
                    {
                        var financial=expansion.Fork();
                        var candidate=financial.TryPlan(observation,profile,nativeProfile,financialSite,FreshFinancialActor,AdmissibleNewSite);
                        var intent=Propose("expansion",candidate,()=>expansion=financial,financial.ObserveReceipt,1);
                        if(intent!=null){expansionAction=candidate;financeIntentAdded=true;}
                    }
                    if(!financeIntentAdded)
                    {
                        // The one fallback cannot reuse the active financial site or an
                        // unchanged expiry tombstone; its ordinary startup must fit free cash
                        // after every hold and the higher protected bands.
                        bool AdmissibleFallbackSite(int id)=>id!=financialSite&&AdmissibleNewSite(id);
                        var ordinary=expansion.Fork();
                        expansionAction=ordinary.TryPlan(observation,profile,nativeProfile,0,FreshActor,AdmissibleFallbackSite);
                        Propose("expansion",expansionAction,()=>expansion=ordinary,ordinary.ObserveReceipt,1,protectedFunds);
                    }
                }
            }
#if UNITY_INCLUDE_TESTS
            if(PreselectionReadbackForTests!=null&&(PreselectionReadbackOwnerFilterForTests==null||PreselectionReadbackOwnerFilterForTests==OwnerId))
            {
                long preselectionStarted=Stopwatch.GetTimestamp();
                bool PreselectionFreshActor(int id)=>armies.ArmyFor(id)==0&&!pendingItems.Any(i=>i.Action.EntityIds.Contains(id));
                bool PreselectionAdmissibleSite(int id)=>id!=financialSite&&AdmissibleNewSite(id);
                var counterfactual=expansion.Fork().TryPlan(observation,profile,nativeProfile,0,PreselectionFreshActor,PreselectionAdmissibleSite);
                var counterfactualIntent=counterfactual==null?null:Proposal("expansion",counterfactual,observation,1);
                if(!AiExpansionFinancial.FullyFundable(budget,protectedFunds,counterfactualIntent))counterfactualIntent=null;
                var counterfactualRejection=counterfactualIntent==null?null:armies.Reject(counterfactualIntent);
                var actualIntent=proposals.SingleOrDefault(p=>p.Policy=="expansion");
                PreselectionReadbackForTests(new PlayableAiPreselectionReadback(observation,budget.Capture(),protectedFunds,
                    expansionFinancial.ActiveSite(arbiter),expansionFinancial.Ready(profile,budget,arbiter,observation.Tick),threatened,urgentRepair,
                    openingExecutor.AllowsExpansion,lastDecisionTick,DecisionIntervalTicks,ObservationDelayTicks,
                    armies.Capture().Armies.SelectMany(a=>a.Members),pendingItems.SelectMany(p=>p.Action.EntityIds),expansion.Capture(),
                    expansion.Fork(),knowledge.Capture(),records,counterfactual,
                    counterfactualIntent==null?null:new PlayableAiIntentReadback(counterfactualIntent,counterfactualRejection),
                    pendingItems.Any(p=>p.PolicyName=="expansion"),actionLimit-pendingItems.Count,expansionAction,
                    actualIntent==null?null:new PlayableAiIntentReadback(actualIntent,armies.Reject(actualIntent))));
                PreselectionReadbackElapsedStopwatchTicksForTests+=Stopwatch.GetTimestamp()-preselectionStarted;
                PreselectionReadbackOwnerCallsForTests++;
            }
#endif
            ProposePositionDevelopment(observation,proposals,positionProtectedFunds,urgentRepair,positionChoices,positionSaving);
            foreach(var candidate in infrastructure.Plan(observation,profile,nativeProfile,EconomyPlan.Capacity).Where(c=>c.Legal&&(c.Action.Kind!=PlayableCommandKind.SellBuilding||!launches.ContainsProducer(c.Action.EntityIds.Single()))))
            {if(!Available("infrastructure"))break;var ip=infrastructure.Fork();Propose("infrastructure",ip.Admit(observation,candidate.Action,budget),()=>infrastructure=ip,ip.ObserveReceipt,candidate.Priority,positionSaving!=null&&candidate.Priority==0?(int?)checked(positionProtectedFunds+positionPrice):null);}
            foreach(var candidate in EconomyPlan.Candidates.Where(c=>c.Legal&&Available(c.Policy)&&!expansion.ClaimsSite(c.Action.SiteId)&&!infrastructure.ClaimsSlot(c.Action.SiteId,c.Action.SlotId)))
            {
                if(positionSaving!=null&&candidate.Policy=="production"&&candidate.Priority<2&&openingExecutor.Capture().InitialForceFulfilled&&launches.ReservationFor(candidate.Action)==null&&
                    !(missingScout&&candidate.Action.UnitKind==PlayableEntityKind.Explorer)&&
                    !positionSaving.AllowsProduction(budget.Available,PlayableUnitRules.Cost(profile,candidate.Action.UnitKind)))continue;
                if(candidate.Policy=="economy")
                {var e=economy.Fork();Propose("economy",e.Admit(observation,candidate.Action),()=>economy=e,e.ObserveReceipt,candidate.Priority,
                    positionSaving!=null?(int?)checked(positionProtectedFunds+positionPrice):null);}
                else
                {var pr=production.Fork();Propose("production",pr.Admit(observation,candidate.Action),()=>production=pr,pr.ObserveReceipt,launches.ReservationFor(candidate.Action)!=null?1:candidate.Priority);}
            }
            var sc=scout.Fork();
            // Detached candidates compete through the arbiter's shared recipient
            // claims; an unselected expansion does not own its proposed actor.
            var scoutAction=sc.Admit(observation,scoutPlanner.Plan(observation,knowledge.Capture(),armies,profile,nativeProfile,ScoutBudget,preparationScoutSite));
            Propose("scout",scoutAction,()=>{scoutPlanner.Commit(observation,scoutAction,armies,preparationScoutSite);scout=sc;},sc.ObserveReceipt);
            // The pure technology plan applies the same productive prerequisite to
            // science infrastructure AND QueueResearch; opening/scout/ledger bands remain.
            var re=research.Fork();Propose("research",re.Admit(observation,TechnologyPlan.Action),()=>research=re,re.ObserveReceipt,0,positionSaving!=null&&!priorityResearch&&!technologyFunding.HasAcceptedScience(budget,arbiter)?(int?)checked(positionProtectedFunds+positionPrice):null);

            var microPlan=combatMicro.Plan(observation,profile,nativeProfile,nativeConfig.Difficulty,armies);
            if(microPlan!=null)Propose(AiCombatMicroPolicy.Policy,microPlan.Action,()=>combatMicro.Commit(microPlan,Generation,observation.Tick),combatMicro.Receipt,microPlan.Survival?2:0);

            var free=observation.Population==null?0:Math.Max(0,observation.Population.Capacity-observation.Population.Living-observation.Population.Reserved);
            int availableActions=actionLimit-pendingItems.Count;
            if(availableActions==0&&da!=null)
            {
                // C3 survival outranks a delayed macro obligation. Release one complete
                // transaction through its ordinary terminal receipt, never exceed C9 capacity.
                var displaced=pendingItems.OrderBy(p=>p.Intent.Priority).ThenByDescending(p=>p.Ordinal).ThenBy(p=>p.PolicyName,StringComparer.Ordinal).First();
                pendingItems.Remove(displaced);Finish(displaced,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Survival defense needs bounded pending attention.");availableActions=1;
            }
            if(availableActions==0)
            {
#if UNITY_INCLUDE_TESTS
                if(FundingReadbacksEnabledFor("DecisionAttentionExit"))FundingReadback("DecisionAttentionExit",new{AvailableActions=availableActions,Pending=pendingItems.Select(p=>new{p.PolicyName,Action=p.Action.Kind,Actors=p.Action.CopyEntityIds()}).ToArray(),TechnologyAction=TechnologyPlan.Action==null?null:new{TechnologyPlan.Action.Kind,TechnologyPlan.Action.ResearchKind},TechnologyReason=TechnologyPlan.Reason,Proposals=proposals.Select(p=>new{p.Policy,p.Action.Kind,p.Action.ResearchKind,p.Credits}).ToArray()});
#endif
                Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);return;
            }
 #if UNITY_INCLUDE_TESTS
            if(FundingReadbacksEnabledFor("ActualCandidatesBeforeSelection"))FundingReadback("ActualCandidatesBeforeSelection",new{Proposals=proposals.Select(p=>new{p.Policy,p.Id,p.Credits,p.StartupFund,p.Priority,p.ReservationId,Kind=p.Action.Kind,Research=p.Action.ResearchKind,Site=p.Action.SiteId}).ToArray(),PriorSelectionRejections=arbiter.Rejections.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>new{x.Key,x.Value}).ToArray(),AvailableActions=availableActions,Ledger=budget.Capture().Reserved.Select(e=>new{e.Id,e.Purpose,e.Amount}).ToArray()});
 #endif
            var selected=arbiter.Select(proposals,snapshot.Tick,observation.Credits,free,availableActions,budget,armies,profile);
#if UNITY_INCLUDE_TESTS
            if(FundingReadbacksEnabledFor("ActualSelection"))FundingReadback("ActualSelection",new{Selected=selected.Select(p=>new{p.Policy,p.Id,p.Credits,Kind=p.Action.Kind,Research=p.Action.ResearchKind,Site=p.Action.SiteId}).ToArray(),Rejections=arbiter.Rejections.OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray()});
#endif
#if UNITY_INCLUDE_TESTS
            if(CurrentExpansionDecisionReadbackForTests!=null&&(CurrentExpansionDecisionOwnerFilterForTests==null||CurrentExpansionDecisionOwnerFilterForTests==OwnerId))
            {
                var actualExpansion=proposals.SingleOrDefault(p=>p.Policy=="expansion");
                CurrentExpansionDecisionReadbackForTests(new CurrentExpansionDecisionReadback(observation,budget.Capture(),protectedFunds,
                    financialSite,financialReady,threatened,urgentRepair,
                    openingExecutor.AllowsExpansion,expansionAction,actualExpansion==null?null:new PlayableAiIntentReadback(actualExpansion,null),
                    proposals.Select(p=>new PlayableAiIntentReadback(p,null)),
                    selected.Select(p=>p.Policy+":"+p.Id+":"+p.Action.Kind+":"+p.Action.SiteId+":"+string.Join(",",p.Action.CopyEntityIds())),arbiter.Rejections,
                    beforeFinanceDetach,afterTechnologyAllocation,afterFinancialAllocation,actualThreats.Select(t=>t.Id).ToArray(),missingScout,
                    essentialScale,scaling.ScalingReason,scaling.StartupFund,armies.Capture().Armies.SelectMany(a=>a.Members).Distinct().OrderBy(id=>id).ToArray(),
                    pendingItems.SelectMany(i=>i.Action.EntityIds).Distinct().OrderBy(id=>id).ToArray(),expansion.Capture()));
            }
#endif
            var decision=++ordinal;int actionOrdinal=0;
            economyDecision=new AiEconomyDecision(observation,profile,nativeProfile,decision,EconomyPlan,selected,arbiter.Rejections,budget);
            foreach(var chosenIntent in selected)
            {
                var intent=chosenIntent;
                if(expansionFinancial.ShouldConsume(intent,budget,arbiter,profile))intent=expansionFinancial.Consume(intent,budget,arbiter,profile);
                bool scoutReusesBuiltExpansion=intent.Policy=="scout"&&intent.Action.Kind==PlayableCommandKind.Move&&intent.Action.EntityIds.Any(expansion.AllowsScoutReuse);
                if(intent.Policy!="expansion"&&!scoutReusesBuiltExpansion&&intent.Action.EntityIds.Any(expansion.ClaimsActor)){expansion.Release("survival preemption");expansion.ReconcileFunding(budget,profile,authorityTick);}
                intent.Commit();var local=intent.Action;
                var identity=new AiReceiptIdentity(Generation,OwnerId,decision,++actionOrdinal);
                bool fundedTechnology=intent.ReservationId!=null&&intent.ReservationId.StartsWith(AiTechnologyFunding.Purpose+":",StringComparison.Ordinal);
                if(fundedTechnology)
                {if(!technologyFunding.Accept(intent,identity,observation,profile,budget,arbiter))throw new InvalidOperationException("Selected technology goal exceeds owner budget.");}
                else
                {
                    if(intent.Policy=="infrastructure")infrastructure.Use(intent,budget);
                    else if(intent.Policy=="expansion")expansion.UseFunding(intent,budget);else launches.Use(intent,identity,budget,profile);
                    if(!budget.Accept(new AiBudgetEntry(intent.Id,intent.Policy,intent.Action,intent.Credits,intent.Priority,snapshot.Tick,intent.ExpiresTick,
                        "terminal receipt / invalid target / expiry",intent.Claims,BudgetTerms,identity)))throw new InvalidOperationException("Selected intent exceeds owner budget.");
                }
                if(intent.Policy=="expansion")expansion.BeginFunding(intent,budget,profile);else launches.Begin(intent,identity,snapshot.Tick,budget,profile,nativeProfile);
                var action=Reidentify(local,allocateAction!=null?allocateAction():checked(decision*actionLimit+actionOrdinal));
                var item=new Pending{Observation=observation,ObservationIdentity=observation.Identity,ObservationTick=observation.Tick,SnapshotSequence=observation.SnapshotSequence,Action=action,DueTick=snapshot.Tick+CombatReaction(action,snapshot.Tick,intent.ExpiresTick),HumanSequence=humanSequence,Receipt=intent.Feedback,Scout=intent.Policy=="scout",PolicyName=intent.Policy,Ordinal=decision,Identity=identity,LocalActionId=local.ActionId,Intent=intent};
                pendingItems.Add(item);
                Emit(item,PlayableAiDeliveryStatus.Scheduled,0,null,null,null,null,"Authority scheduled.");
                Record(new PlayableAiTraceRecord(observation.Identity,action.ActionId,item.DueTick,0,0,PlayableAiDeliveryStatus.Scheduled,null,item.PolicyName==AiArmyPlanner.Policy?armyPlanner.Capture().Reason:"Authority scheduled.",OwnerId,action.SourceIdentity,identity,item.PolicyName,action.Kind));
            }
            Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);
        }
        private bool RecentGroupOrder(PlayableAiObservation o,PlayableEntitySnapshot u)
        {
            if(u.CurrentOrder!=null&&o.Tick-u.CurrentOrder.IssuedTick<repeatTicks)return true;
            // The native authority's persistent stamp survives arrival and Stop. It is already
            // saved with the world and projected only for own units; no second attention clock.
            var stamp=u.OrderStamp;
            return stamp!=null&&stamp.UnitId==u.Id&&stamp.Owner==o.Owner&&stamp.Generation==Generation&&
                stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Source==opening.SourceIdentity&&stamp.ActionId>0&&stamp.JobId>0&&
                stamp.Tick>=0&&stamp.Tick<=o.Tick&&o.Tick-stamp.Tick<repeatTicks;
        }
        private static bool Tactical(PlayableAiAction a)=>a.Kind==PlayableCommandKind.Move||a.Kind==PlayableCommandKind.Attack||a.Kind==PlayableCommandKind.AttackMove||a.Kind==PlayableCommandKind.Stop||a.Kind==PlayableCommandKind.Follow;
        private static bool SameOrder(PlayableAiAction a,PlayableEntitySnapshot u,string policy,bool missionApplied,string owner,long generation)
        {
            var pending=u.Queue?.Pending;
            bool sameLiveExpansionMove=policy=="expansion"&&a.PlayerId==owner&&a.Generation==generation&&a.SourceIdentity==PlayableAiOpeningComposition.SourceIdentity&&
                a.Kind==PlayableCommandKind.Move&&a.SiteId>0&&a.EntityIds.Contains(u.Id)&&u.Queue?.PendingAiRouteViable==true&&
                pending!=null&&pending.AiActionId>0&&pending.Kind==PlayableCommandKind.Move&&pending.PublicObjectiveId==a.SiteId&&
                pending.Anchor.HasValue&&pending.Anchor.Value.Position.Equals(a.Target);
            bool sameLiveAttackMove=AiArmyPlanner.IsArmyPolicy(policy)&&missionApplied&&a.PlayerId==owner&&a.Generation==generation&&a.Kind==PlayableCommandKind.AttackMove&&a.SiteId>0&&
                u.Queue?.PendingAiRouteViable==true&&pending!=null&&pending.AiActionId>0&&pending.Kind==PlayableCommandKind.AttackMove&&
                pending.PublicObjectiveId==a.SiteId&&pending.Anchor.HasValue&&pending.Anchor.Value.Position.Equals(a.Target)&&
                a.SourceIdentity==PlayableAiOpeningComposition.SourceIdentity;
            bool sameLiveMissionMove=AiArmyPlanner.IsArmyPolicy(policy)&&missionApplied&&a.PlayerId==owner&&a.Generation==generation&&a.Kind==PlayableCommandKind.Move&&
                u.Queue?.PendingAiRouteViable==true&&pending!=null&&pending.AiActionId>0&&pending.Kind==PlayableCommandKind.Move&&
                pending.Anchor.HasValue&&pending.Anchor.Value.Position.Equals(a.Target)&&a.SourceIdentity==PlayableAiOpeningComposition.SourceIdentity;
            if(sameLiveExpansionMove||sameLiveAttackMove||sameLiveMissionMove)return true;
            return (u.Queue?.Pending==null&&(u.Queue==null||u.Queue.Deferred.Count==0))&&(a.Kind==PlayableCommandKind.Stop?u.CurrentOrder==null&&!u.Held:
                u.CurrentOrder!=null&&(a.Kind==PlayableCommandKind.Follow?u.CurrentOrder.Kind==PlayableTacticalOrderKind.Follow&&u.CurrentOrder.TargetId==a.TargetId:
                a.Kind==PlayableCommandKind.Attack?u.CurrentOrder.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==a.TargetId:
                u.CurrentOrder.Kind==(a.Kind==PlayableCommandKind.Move?PlayableTacticalOrderKind.Move:PlayableTacticalOrderKind.AttackMove)&&u.CurrentOrder.Destination.Equals(a.Target)));
        }
        private int BudgetCost(PlayableAiAction action)
        {
            if(action.Kind==PlayableCommandKind.BuildAt)return TerritoryRules.Cost(profile,action.BuildingKind);
            if(action.Kind==PlayableCommandKind.UpgradeRefinery)return (int)Math.Ceiling(profile.RefineryUpgradeCost);
            if(AiRosterCatalog.IsProduction(action.Kind))
                return (int)roster.CreditCost(roster.Production(action.Kind).kind,profile);
            if(action.Kind==PlayableCommandKind.QueueResearch)return (int)Math.Ceiling(AiResearchCatalog.For(action.ResearchKind).Cost(profile));
            return 0;
        }
        private bool BudgetReservationValid(AiBudgetEntry entry,PlayableAiObservation o)=>AiExpansionFinancial.IsFunding(entry)?expansionFinancial.Valid(entry,arbiter):AiTechnologyFunding.IsFunding(entry)?technologyFunding.Valid(entry,o,profile,budget,arbiter):AiExpansionPlanner.IsFunding(entry)?expansion.FundTargetValid(entry,o):BudgetTargetValid(entry.Action,o);
        private static bool BudgetTargetValid(PlayableAiAction action,PlayableAiObservation observation)=>
            action.EntityIds.All(id=>observation.Entities.Any(x=>x.Id==id&&x.Owner==observation.Owner&&x.Health>0)||observation.Buildings.Any(x=>x.Id==id&&x.Owner==observation.Owner&&x.Health>0))&&
            (action.Kind!=PlayableCommandKind.BuildAt||observation.Sites.Any(x=>x.Site.Id==action.SiteId&&(x.Owner==observation.Owner||action.SlotId==0&&x.Claimant==observation.Owner&&x.Progress>=1))&&
                (action.ParentId==0||observation.Buildings.Any(x=>x.Id==action.ParentId&&x.Owner==observation.Owner&&x.Health>0)));
        private AiIntent Proposal(string policyName,PlayableAiAction action,PlayableAiObservation o,int priority,bool pendingRepairHandoff=false)
        {
            // Incidental capture is not permission for a second unselected remote income build.
            if(policyName=="economy"&&action.Kind==PlayableCommandKind.BuildAt&&action.SlotId==0&&
                (action.BuildingKind==PlayableBuildingKind.Mine||action.BuildingKind==PlayableBuildingKind.Outpost))
            {
                var target=AiIncomeSiteSelection.Select(o,profile,nativeProfile,knowledge.Capture(),
                    o.Sites.Where(s=>AiIncomeSiteSelection.Free(s,o,profile)&&AiIncomeSiteSelection.SiteSafe(o,profile,knowledge.Capture(),s)),
                    expansionFinancial.RemotePreference(o,nativeProfile,arbiter));
                int committed=expansionFinancial.ActiveSite(arbiter);
                if(action.SiteId!=(committed!=0?committed:target?.Site.Id??0))return null;
                var site=o.Sites.FirstOrDefault(s=>s.Site.Id==action.SiteId);
                if(site==null||!AiIncomeSiteSelection.SiteSafe(o,profile,knowledge.Capture(),site)||!AiIncomeSiteSelection.TerritoryAllowed(o,profile,nativeProfile,knowledge.Capture(),site))return null;
            }
            if(action.Kind==PlayableCommandKind.BuildAt&&action.BuildingKind==PlayableBuildingKind.Factory&&doctrine.Ready)
            {
                var kind=doctrine.GroundProductionKind(o,nativeProfile);
                action=new PlayableAiAction(action.ActionId,action.PlayerId,action.ProfileId,action.ProfileRevision,action.Generation,action.SnapshotSequence,action.Kind,action.CopyEntityIds(),action.Target,action.SiteId,action.SlotId,action.BuildingKind,action.ParentId,action.TargetId,action.ProductionOrderId,kind,action.ResearchKind,action.Seed,action.SourceIdentity);
            }
            var actualAdmissionRejection=AiEconomyAdmission.Reject(o,profile,action,catalog:roster);
            if(actualAdmissionRejection!=null)
            {
#if UNITY_INCLUDE_TESTS
                if(policyName=="expansion"&&ProposalAdmissionReadbackForTests!=null&&(ProposalAdmissionOwnerFilterForTests==null||ProposalAdmissionOwnerFilterForTests==OwnerId))
                    ProposalAdmissionReadbackForTests(new PlayableAiProposalAdmissionReadback(OwnerId,o.Tick,policyName,action,"economy-admission",actualAdmissionRejection));
#endif
                return null;
            }
            int cost=0,pop=0;var claims=action.EntityIds.Select(id=>"recipient:"+id).ToList();
            if(AiRepairUsePolicy.IsAction(o,action))claims.Add("repair-tower:"+action.ParentId);
            if(action.Kind==PlayableCommandKind.UpgradeRefinery)cost=(int)Math.Ceiling(profile.RefineryUpgradeCost);
            // Wire v2 has one pending callback per policy. Alternative candidates compete
            // for that policy as well as their physical recipients/slots.
            claims.Add("policy:"+policyName);
            if(policyName=="expansion")
            claims.Add("expansion-site:"+action.SiteId);
            bool queue=AiRosterCatalog.IsProduction(action.Kind);
            if(queue)
            {
                var kind=roster.Production(action.Kind).kind;
                cost=(int)roster.CreditCost(kind,profile);pop=roster.PopulationCost(kind,profile);
            }
            if(action.Kind==PlayableCommandKind.StartBuildingRepair)
                claims.Add("repair:"+action.EntityIds.Single());
            if(action.Kind==PlayableCommandKind.StartBuildingRepair)
                cost=(int)Math.Ceiling(o.Buildings.Single(b=>b.Id==action.EntityIds.Single()).PrivateState.Lifecycle.RepairAllocation);
            if(action.Kind==PlayableCommandKind.SellBuilding)claims.Add("slot:"+action.SiteId+":"+action.SlotId);
            if(action.Kind==PlayableCommandKind.BuildAt)
            {
                cost=TerritoryRules.Cost(profile,action.BuildingKind);claims.Add("slot:"+action.SiteId+":"+action.SlotId);
                var site=o.Sites.SingleOrDefault(x=>x.Site.Id==action.SiteId);
                if(site==null||site.Contested||o.Buildings.Any(b=>b.SiteId==action.SiteId&&b.SlotId==action.SlotId))
                {
#if UNITY_INCLUDE_TESTS
                    if(policyName=="expansion"&&ProposalAdmissionReadbackForTests!=null&&(ProposalAdmissionOwnerFilterForTests==null||ProposalAdmissionOwnerFilterForTests==OwnerId))
                        ProposalAdmissionReadbackForTests(new PlayableAiProposalAdmissionReadback(OwnerId,o.Tick,policyName,action,"build-site-admission",site==null?"site missing":site.Contested?"site contested":"slot occupied"));
#endif
                    return null;
                }
            }
            if(action.Kind==PlayableCommandKind.QueueResearch)
            {
                var researchCost=o.ResearchAvailability.SingleOrDefault(x=>x.Kind==action.ResearchKind);
                if(researchCost==null||!researchCost.Available||!PlayableResearchRules.Prerequisite(action.ResearchKind,o.OwnerResearch)||!AiTechnologyPlanner.ArmySizeAllows(o,profile,action.ResearchKind,nativeProfile))
                {
#if UNITY_INCLUDE_TESTS
                    if(policyName=="expansion"&&ProposalAdmissionReadbackForTests!=null&&(ProposalAdmissionOwnerFilterForTests==null||ProposalAdmissionOwnerFilterForTests==OwnerId))
                        ProposalAdmissionReadbackForTests(new PlayableAiProposalAdmissionReadback(OwnerId,o.Tick,policyName,action,"research-availability",researchCost==null?"descriptor missing":"unavailable"));
#endif
                    return null;
                }cost=(int)Math.Ceiling(researchCost.Cost);
            }
            if(action.EntityIds.Any(id=>!o.Entities.Any(u=>u.Id==id&&u.Owner==o.Owner&&u.Health>0)&&!o.Buildings.Any(b=>b.Id==id&&b.Owner==o.Owner&&b.Health>0)))
            {
#if UNITY_INCLUDE_TESTS
                if(policyName=="expansion"&&ProposalAdmissionReadbackForTests!=null&&(ProposalAdmissionOwnerFilterForTests==null||ProposalAdmissionOwnerFilterForTests==OwnerId))
                    ProposalAdmissionReadbackForTests(new PlayableAiProposalAdmissionReadback(OwnerId,o.Tick,policyName,action,"actor-admission","actor missing, foreign, or dead"));
#endif
                return null;
            }
            // Same effective tactical order is a no-op, even after cooldown. A changed order must
            // also respect the profile recipient interval (survival commands may preempt it).
            if(Tactical(action))
            {
                var units=o.Entities.Where(u=>action.EntityIds.Contains(u.Id)).ToArray();
                if(units.Length==0||units.All(u=>SameOrder(action,u,policyName,policyName==AiArmyPlanner.FlankPolicy?flankPlanner.HasAppliedOrder:armyPlanner.HasAppliedOrder,OwnerId,Generation)))
                {
#if UNITY_INCLUDE_TESTS
                    if(policyName=="expansion"&&ProposalAdmissionReadbackForTests!=null&&(ProposalAdmissionOwnerFilterForTests==null||ProposalAdmissionOwnerFilterForTests==OwnerId))
                        ProposalAdmissionReadbackForTests(new PlayableAiProposalAdmissionReadback(OwnerId,o.Tick,policyName,action,"same-effective-order",units.Length==0?"no projected actor":"all actors already match"));
#endif
                    return null;
                }
                // Attention belongs to the durable group, not the selected subset. A different
                // member cannot bypass its army's cooldown; survival may still preempt it.
                var groupIds=new HashSet<int>(action.EntityIds);
                var armyIds=new HashSet<long>(action.EntityIds.Select(armies.ArmyFor).Where(id=>id!=0));
                foreach(var a in armies.Capture().Armies.Where(a=>armyIds.Contains(a.Id)))groupIds.UnionWith(a.Members);
                if(priority<2&&o.Entities.Any(u=>groupIds.Contains(u.Id)&&RecentGroupOrder(o,u)))
                {
#if UNITY_INCLUDE_TESTS
                    if(policyName=="expansion"&&ProposalAdmissionReadbackForTests!=null&&(ProposalAdmissionOwnerFilterForTests==null||ProposalAdmissionOwnerFilterForTests==OwnerId))
                        ProposalAdmissionReadbackForTests(new PlayableAiProposalAdmissionReadback(OwnerId,o.Tick,policyName,action,"group-order-cooldown","actual recipient/army order interval active"));
#endif
                    return null;
                }
            }
            string key=AiEconomyAdmission.IntentId(policyName,action);
            // Relevant admission condition changes reopen bounded retries; tick, snapshot and
            // unrelated income do not reset a recipient/placement rejection.
            string conditions=(o.Credits>=cost)+":"+(o.Population!=null&&Math.Max(0,o.Population.Capacity-o.Population.Living-o.Population.Reserved)>=pop?1:0)+":"+
                string.Join(",",o.Buildings.Where(b=>b.SiteId==action.SiteId||action.EntityIds.Contains(b.Id)).OrderBy(b=>b.Id).Select(b=>b.Id+"/"+b.Phase+"/"+(b.PrivateState?.QueueCount??0)+(policyName=="infrastructure"?"/"+b.Health+"/"+b.PrivateState?.Lifecycle?.RepairBlockedReason+"/"+b.PrivateState?.Lifecycle?.SaleBlockedReason:"")))+":"+
                string.Join(",",o.Entities.Where(u=>action.EntityIds.Contains(u.Id)||u.Id==action.TargetId).OrderBy(u=>u.Id).Select(u=>u.Id+"/"+u.CurrentOrder?.Kind+"/"+u.CurrentOrder?.TargetId));
            if(pendingRepairHandoff)conditions=AiCombatMicroPolicy.PendingRepairHandoff+conditions;
            long startup=policyName=="expansion"&&action.Kind==PlayableCommandKind.Move&&!expansion.HasFundingFor(action,budget)?(long)TerritoryRules.Cost(profile,action.BuildingKind):action.Kind==PlayableCommandKind.BuildAt&&action.BuildingKind==PlayableBuildingKind.Factory&&o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory)?(long)nativeProfile.Value("economy.factoryLaunchCycles")*PlayableUnitRules.Cost(profile,action.UnitKind):0;
            double utility=policyName=="research"?TechnologyPlan.Branches.Single(b=>b.Descriptor.Kind==action.ResearchKind).Utility:
                action.Kind==PlayableCommandKind.BuildAt&&action.BuildingKind==PlayableBuildingKind.ScientificCenter?TechnologyPlan.Infrastructure?.Utility??1:1;
            string reason=policyName=="research"||action.Kind==PlayableCommandKind.BuildAt&&action.BuildingKind==PlayableBuildingKind.ScientificCenter?TechnologyPlan.Reason:null;
            return new AiIntent(key,policyName,action,checked(o.Tick+DecisionIntervalTicks+ObservationDelayTicks),cost,pop,claims,priority,utility,conditions,reason,startupFund:startup,reservationId:technologyFunding.ReservationFor(action,budget,arbiter)??(policyName=="expansion"?expansionFinancial.ReservationFor(action,o,profile,budget,arbiter):null)??(policyName=="infrastructure"?infrastructure.ReservationFor(action,budget):policyName=="expansion"?expansion.ReservationFor(action,budget):launches.ReservationFor(action)),overdue:policyName=="production"&&priority==1&&launches.ReservationFor(action)==null||policyName==AiArmyPlanner.Policy&&o.Tick-armyPlanner.Capture().LastInitiativeTick>=AiProfile.SecondsToTicks(nativeProfile.Value("armies.offensiveOverdueSeconds"),30));
        }
        private static PlayableAiAction Reidentify(PlayableAiAction a,long id)=>new PlayableAiAction(id,a.PlayerId,a.ProfileId,a.ProfileRevision,a.Generation,a.SnapshotSequence,a.Kind,a.CopyEntityIds(),a.Target,a.SiteId,a.SlotId,a.BuildingKind,a.ParentId,a.TargetId,a.ProductionOrderId,a.UnitKind,a.ResearchKind,a.Seed,a.SourceIdentity);
        internal void Stop(long? tick=null){ReleaseExecutionAuthority();if(tick.HasValue)authorityTick=tick.Value;while(pending!=null)Cancel(PlayableAiDeliveryStatus.Stopped,"Runtime stopped.");combatMicro.Stop();budget.ReleaseAllReservations();arbiter.ClearFundingMemory();arbiter.ClearSiteFundingMemory();arbiter.ClearPositionSaving();launches.Clear(budget);expansion.Release("runtime stopped");infrastructure.Release("runtime stopped");armies.DisbandAll();armyPlanner.Stop();flankPlanner.Stop();defense.Stop();Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);}
        private void Cancel(PlayableAiDeliveryStatus status,string message)
        =>CancelItem(pending,status,message);
        private void CancelItem(Pending item,PlayableAiDeliveryStatus status,string message)
        {pendingItems.Remove(item);Finish(item,0,0,status,status==PlayableAiDeliveryStatus.Stopped?PlayableCommandStatus.Stopped:PlayableCommandStatus.Cancelled,message);}
        private void Finish(Pending item,long sequence,long applicationTick,PlayableAiDeliveryStatus status,PlayableCommandStatus runtimeStatus,string message,bool gameplayPaid=true)
        {
            var record=new PlayableAiTraceRecord(item.ObservationIdentity,item.Action.ActionId,item.DueTick,sequence,
                applicationTick,status,runtimeStatus,message,OwnerId,item.Action.SourceIdentity,item.Identity,item.PolicyName,item.Action.Kind);
            Emit(item,status,sequence,sequence!=0||status==PlayableAiDeliveryStatus.Stale?(long?)authorityTick:null,authorityTick,status==PlayableAiDeliveryStatus.Applied?(long?)applicationTick:null,runtimeStatus,message);
            lastPolicy=item.PolicyName;lastActionKind=item.Action.Kind;
            arbiter.Terminal(item.Intent,authorityTick,status);
            budget.Reconcile(item.Intent.Id,item.Identity,status,gameplayPaid);
            technologyFunding.Terminal(item.Intent,status,budget,arbiter);
            launches.Terminal(item.Intent,item.Identity,status,gameplayPaid,budget,profile);
            var terminalReceipt=new PlayableAiTraceRecord(record.ObservationIdentity,item.LocalActionId,record.DueTick,sequence,applicationTick,status,runtimeStatus,message,OwnerId,item.Action.SourceIdentity,item.Identity,item.PolicyName,item.Action.Kind);item.Receipt(terminalReceipt);
            if(item.PolicyName=="expansion"&&item.Action.Kind==PlayableCommandKind.BuildAt&&status==PlayableAiDeliveryStatus.Applied)
                scoutPlanner.ReleasePreparation(armies,item.Action.SiteId,item.Action.EntityIds.SingleOrDefault());
            openingExecutor.ObserveReceipt(item.Action,terminalReceipt);if(item.PolicyName=="expansion")expansion.TerminalFunding(item.Intent,status,budget,profile);expansion.ReconcileFunding(budget,profile,authorityTick,pendingItems.Select(x=>x.Intent));Record(record);
#if DEVELOPMENT_BUILD
            Console.WriteLine("[Spacewars owner AI] owner="+OwnerId+" source="+PlayableAiOpeningComposition.SourceIdentity+" profile="+profile.ProfileId+"@"+profile.Revision+
                " seed="+opening.MatchSeed+" policy="+item.PolicyName+" kind="+item.Action.Kind+" status="+status+" generation="+Generation+
                " observation="+item.ObservationIdentity+" action="+item.Action.ActionId+" due="+item.DueTick+" sequence="+sequence+" tick="+applicationTick+
                " commandStatus="+runtimeStatus+" decisionOrdinal="+item.Ordinal+" observationTick="+item.ObservationTick+
                " actionTick="+item.ObservationTick+" receiptTick="+authorityTick+" applicationTick="+
                (status==PlayableAiDeliveryStatus.Applied?applicationTick.ToString(System.Globalization.CultureInfo.InvariantCulture):"none"));
#endif
        }
        private void Emit(Pending item,PlayableAiDeliveryStatus status,long sequence,long? delivery,long? receipt,long? application,PlayableCommandStatus? runtimeStatus,string message)
        {if(item.Observation!=null)audit?.Invoke(new PlayableAiEvaluationEvent(item.Observation,item.Action,item.PolicyName,item.Ordinal,item.DueTick,status,sequence,delivery,receipt,application,runtimeStatus,message));}
        private void Record(PlayableAiTraceRecord record)
        {records.Add(record);if(records.Count>128)records.RemoveAt(0);Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);}
        private PlayableAiOwnerCheckpoint Capture()=>new PlayableAiOwnerCheckpoint(OwnerId,PlayableAiOpeningComposition.SourceIdentity,profile.ProfileId,profile.Revision,opening.MatchSeed,Generation,lastDecisionTick,lastScoutTick,
            pending?.Action.ActionId??0,pending?.DueTick??0,lastCommandSequence,lastPolicy,lastActionKind,opening,mission.Mission,midgame.Capture(),artillery.Capture(),records,economyDecision,lastScoutOrderTick).BindExpansion(expansion.Capture()).BindInfrastructure(infrastructure.Capture()).BindArmies(armies.Capture()).BindArmyMission(armyPlanner.Capture()).BindKnowledge(knowledge.Capture()).BindDoctrine(doctrine.Capture()).BindOpeningExecution(openingExecutor.Capture()).BindComposition(composition.Capture(),compositionAssessment);
    }
}

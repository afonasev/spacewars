using System;
using System.Collections.Generic;
using System.Linq;
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
    internal sealed partial class PlayableAiOwnerLoop
    {
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
        internal void Rebind(PlayableProfile next){if(WorldWire.Binding(profile).SequenceEqual(WorldWire.Binding(next)))return;while(pending!=null)Cancel(PlayableAiDeliveryStatus.Cancelled,"Balance revision changed.");profile=next;armyPlanner.Rebind(next);budget.RepriceReservations(e=>launches.IsStartup(e)?launches.Reprice(e,next):infrastructure.IsConversion(e)?infrastructure.Reprice(e,next):BudgetCost(e.Action),BudgetTerms);infrastructure.Rebind(next,budget,authorityTick);launches.ReconcileFunding(budget,next,authorityTick);expansion.ReconcileFunding(budget,next,authorityTick,pendingItems.Select(x=>x.Intent));production.Rebind(next);mission.Rebind(next);midgame.Rebind(next);artillery.Rebind(next);Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);}
        private AiKnowledgeTracker knowledge;
        private AiProfile nativeProfile;
        private readonly AiRosterCatalog roster=AiRosterCatalog.Initial;
        private AiOwnerConfig nativeConfig;
        private readonly PlayableAiOpeningCompositionState opening;
        private readonly AiOpeningExecutor openingExecutor;
        private PlayableAiEconomicLivenessPolicy economy;
        internal AiEconomyPlan EconomyPlan{get;private set;}
        private AiEconomyDecision economyDecision;
        private PlayableAiProductionLivenessPolicy production;
        private PlayableAiResearchLivenessPolicy research;
        private readonly PlayableAiResearchReadinessTracker readiness=new PlayableAiResearchReadinessTracker();
        private PlayableAiScoutLivenessPolicy scout;
        private readonly AiScoutPlanner scoutPlanner=new AiScoutPlanner();
        private int ScoutBudget=>scoutPlanner.Budget(nativeProfile,nativeConfig.Difficulty,nativeConfig.PersonalitySeed,openingExecutor.ScoutPlan);
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
        private AiDefensePlanner defense;
        private AiProductionDemand demand=new AiProductionDemand();
        private AiFactoryLaunchCommitments launches=new AiFactoryLaunchCommitments();
        private AiExpansionPlanner expansion;
        private AiInfrastructurePlanner infrastructure;
        internal AiBudgetLedger Budget=>budget;
        private string BudgetTerms=>profile.ProfileId+"@"+profile.Revision;
        internal void ReconcileBudget(PlayableDomain domain)=>domain.ReconcileAiPayments(budget);
        internal void ValidateBudget(PlayableDomain domain)
        {launches.Validate(budget,pendingItems.Select(x=>x.Intent).ToArray(),pendingItems.Select(x=>x.Identity).ToArray(),profile,domain.Tick);expansion.ValidateFunding(budget,pendingItems.Select(x=>x.Intent).ToArray(),profile,Generation);infrastructure.ValidateFunding(budget,pendingItems.Select(x=>x.Intent).ToArray(),profile,domain.Tick);AiStateWire.Require(pendingItems.Where(x=>x.Intent.ReservationId!=null).All(x=>x.PolicyName=="production"||x.PolicyName=="expansion"||x.PolicyName=="infrastructure"),"unknown reservation policy");domain.ValidateAiBudget(budget);domain.ValidateAiProductionDemand(demand,OwnerId);domain.ValidateAiArmies(armies);domain.ValidateAiKnowledge(knowledge.Capture());}
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
            OwnerId=opening.OwnerId;openingExecutor=new AiOpeningExecutor(opening,generation);knowledge=new AiKnowledgeTracker(OwnerId,generation);
            nativeConfig=new AiOwnerConfig(OwnerId,aiDifficulty,opening.MatchSeed,nativeProfile);
            arbiter=new AiDecisionArbiter(nativeProfile);actionLimit=(int)nativeProfile.DifficultyValue(aiDifficulty,"actionsPerDecision");
            repeatTicks=AiProfile.SecondsToTicks(nativeProfile.Value("decision.repeatOrderSeconds"),30);
            Generation=generation;mission=new PlayableAiMissionDefensePolicy(profile,ownerId:OwnerId);midgame=new PlayableAiMidgameStrategyPolicy(profile,OwnerId);artillery=new PlayableAiArtillerySupportPolicy(profile,ownerId:OwnerId);
            budget=new AiBudgetLedger(OwnerId,generation,AiProfile.SecondsToTicks(nativeProfile.Value("economy.reservationExpirySeconds"),30));
            armies=new AiArmyRegistry(OwnerId,generation,nativeProfile,aiDifficulty);
            armyPlanner=new AiArmyPlanner(OwnerId,generation);armyPlanner.Rebind(profile);defense=new AiDefensePlanner(OwnerId,generation);
            expansion=new AiExpansionPlanner(OwnerId);infrastructure=new AiInfrastructurePlanner(OwnerId);
            economy=new PlayableAiEconomicLivenessPolicy(ownerId:OwnerId);research=new PlayableAiResearchLivenessPolicy(ownerId:OwnerId);scout=new PlayableAiScoutLivenessPolicy(ownerId:OwnerId);
            production=new PlayableAiProductionLivenessPolicy(profile:profile,ownerId:OwnerId,catalog:roster);
            Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);
        }
        internal long Generation{get;}
        internal PlayableAiOwnerCheckpoint Checkpoint{get;private set;}
        internal void Deliver(PlayableDomain domain,long humanSequence,bool paused)
        {
            ReconcileBudget(domain);
            while(pending!=null)
            {
                var before=pending;DeliverOne(domain,humanSequence,paused);
                if(ReferenceEquals(before,pending))break;
            }
        }
        private void DeliverOne(PlayableDomain domain,long humanSequence,bool paused)
        {
            authorityTick=domain.Tick;
            if(!domain.Authorizes(OwnerId)){Stop(domain.Tick);return;}
            if(pending==null)return;
            if(paused){Cancel(PlayableAiDeliveryStatus.Cancelled,"Runtime paused.");return;}
            if(domain.Outcome!=PlayableMatchOutcome.Playing){Cancel(PlayableAiDeliveryStatus.Stopped,"Match finished.");return;}
            if(humanSequence!=pending.HumanSequence){Cancel(PlayableAiDeliveryStatus.Cancelled,"Newer human command has priority.");return;}
            if(domain.Tick<pending.DueTick)return;
            var item=pending;pendingItems.Remove(item);
            var action=item.Action;
            if(domain.Tick>item.Intent.ExpiresTick){Finish(item,0,0,PlayableAiDeliveryStatus.Stale,PlayableCommandStatus.StaleGeneration,"Intent expired before delivery.");return;}
            if(action.Generation!=Generation||action.PlayerId!=OwnerId||action.Seed!=opening.MatchSeed||action.SourceIdentity!=opening.SourceIdentity||action.ProfileId!=profile.ProfileId||action.ProfileRevision!=profile.Revision||action.SnapshotSequence!=item.SnapshotSequence)
            {
                Finish(item,0,0,PlayableAiDeliveryStatus.Stale,PlayableCommandStatus.StaleGeneration,"Observation binding changed.");return;
            }
            string ownership=armies.Reject(item.Intent);
            if(ownership!=null){Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,ownership);return;}
            if(item.PolicyName==AiArmyPlanner.Policy&&(action.Kind==PlayableCommandKind.Move||action.Kind==PlayableCommandKind.Follow)&&
                (action.Kind==PlayableCommandKind.Follow&&(armies.ArmyFor(action.TargetId)==0||action.EntityIds.Any(id=>armies.ArmyFor(id)!=armies.ArmyFor(action.TargetId))||armies.Capture().Armies.Single(a=>a.Id==armies.ArmyFor(action.TargetId)).Reinforcements.Contains(action.TargetId))||!(action.Kind==PlayableCommandKind.Move&&armyPlanner.Capture().Phase==AiArmyPhase.Retreating&&armyPlanner.Capture().Reason=="withdrawal"&&action.EntityIds.All(id=>armies.ArmyFor(id)==armyPlanner.Capture().ArmyId&&!armies.Capture().Armies.Single(a=>a.Id==armyPlanner.Capture().ArmyId).Reinforcements.Contains(id))?domain.ArmyWithdrawalSafe(action):domain.ArmyMoveSafe(action))))
            {Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Current observed army path is no longer safe or reachable.");return;}
            // Pending tactical proposals can become redundant during their reaction delay.
            // Check the actual owner projection before allocating a command sequence.
            if(Tactical(action))
            {
                var delivery=PlayableAiObservation.From(domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId)));
                var recipients=delivery.Entities.Where(u=>u.Owner==delivery.Owner&&u.Health>0&&action.EntityIds.Contains(u.Id)).ToArray();
                if(recipients.Length==action.EntityIds.Count&&recipients.Length>0&&recipients.All(u=>SameOrder(action,u)))
                {Finish(item,0,domain.Tick,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Effective tactical order already matches at delivery.");return;}
            }
            long sequence=domain.AllocateAiSequence();lastCommandSequence=sequence;
            var command=new PlayableCommand(action.Generation,sequence,action.PlayerId,action.Kind,action.CopyEntityIds(),action.Target,
                siteId:action.SiteId,slotId:action.SlotId,buildingKind:action.BuildingKind,parentId:action.ParentId,targetId:action.TargetId,
                productionOrderId:action.ProductionOrderId,unitKind:action.UnitKind,researchKind:action.ResearchKind,origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:action.ActionId,actionId:action.ActionId);
            PlayableCommandStatus status;string message;
            try{status=domain.Apply(command,out message);}
            catch(Exception ex){Finish(item,sequence,domain.Tick,PlayableAiDeliveryStatus.Rejected,PlayableCommandStatus.Rejected,ex.ToString());throw;}
            if(status==PlayableCommandStatus.Applied)domain.BindAiRepairPayment(action,item.Identity);
            // Preserve command cadence as a separate receipt clock, never intel success.
            if(item.Scout&&item.Action.Kind==PlayableCommandKind.Move&&status==PlayableCommandStatus.Applied)lastScoutOrderTick=domain.Tick;
            budget.ObserveBank(domain.AiLiquidCredits(OwnerId));
            Finish(item,sequence,domain.Tick,status==PlayableCommandStatus.Applied?PlayableAiDeliveryStatus.Applied:PlayableAiDeliveryStatus.Rejected,status,message,domain.AiCommandPaid(action));
        }
        internal AiArmyRegistryState ArmyState=>armies.Capture();
        internal bool NeedsArmyRoutes(long tick)=>tick-lastDecisionTick>=DecisionIntervalTicks||
            armies.HasReinforcements&&records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied&&
                (r.Kind==PlayableCommandKind.Attack||r.Kind==PlayableCommandKind.AttackMove)&&r.ApplicationTick==tick-1);
        internal bool NeedsDefenseRoutes(PlayableSnapshot snapshot)=>!defense.EmergencyObserved&&AiDefensePlanner.Threats(PlayableAiObservation.From(snapshot),profile).Length>0;
        private static bool Offensive(Pending p)=>p.PolicyName==AiArmyPlanner.Policy||p.PolicyName=="artillery"||p.PolicyName=="expansion";
        private void GuardDefense(PlayableAiObservation o)
        {
            var threats=AiDefensePlanner.Threats(o,profile);
            foreach(var item in pendingItems.Where(p=>threats.Length>0&&Offensive(p)||p.PolicyName==AiDefensePlanner.Policy&&!threats.Any(t=>t.Id==p.Action.TargetId)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Observed center threat changed tactical priority.");}
        }
        internal void GuardDefenseDelivery(PlayableDomain domain)
        {
            if(!pendingItems.Any(p=>Offensive(p)||p.PolicyName==AiDefensePlanner.Policy)||!domain.Authorizes(OwnerId)||domain.Outcome!=PlayableMatchOutcome.Playing)return;
            authorityTick=domain.Tick;
            GuardDefense(PlayableAiObservation.From(domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId))));
        }
        internal void Review(PlayableSnapshot snapshot,long humanSequence)
        {
            authorityTick=snapshot.Tick;
            if(snapshot.OwnerEliminated||snapshot.Generation!=Generation||snapshot.Status==RuntimeStatus.Stopped||snapshot.Status==RuntimeStatus.Failed||snapshot.Outcome!=PlayableMatchOutcome.Playing){while(pending!=null)Cancel(PlayableAiDeliveryStatus.Stopped,"Match ended.");armies.DisbandAll();armyPlanner.Stop();defense.Stop();Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);return;}
            if(snapshot.Paused)return;
            var current=PlayableAiObservation.From(snapshot);
            var newCoverage=knowledge.Observe(current,nativeProfile);
            if(newCoverage.Any(id=>current.PublicScoutObjectives.Any(p=>p.SiteId==id)))lastScoutTick=current.Tick;
            GuardDefense(current);
            armies.Observe(current);
            scoutPlanner.Observe(current,knowledge.Capture(),armies,nativeProfile,ScoutBudget);
            foreach(var item in pendingItems.Where(x=>x.Scout&&x.Action.EntityIds.Any(id=>armies.ArmyFor(id)==0)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Scout assignment released.");}
            armyPlanner.Observe(current,armies,nativeProfile,records);
            bool newEmergency=defense.Observe(current,armies,profile);
            if(armyPlanner.Active)AiTacticalExecutor.Observe(current,armies,armyPlanner.Capture().ArmyId,profile,
                pendingItems.SelectMany(x=>x.Action.EntityIds).Concat(current.Entities.Where(u=>expansion.ClaimsActor(u.Id)).Select(u=>u.Id)));
            foreach(var item in pendingItems.Where(x=>x.PolicyName==AiDefensePlanner.Policy&&!defense.Active).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Defense army released.");}
            foreach(var item in pendingItems.Where(x=>x.PolicyName==AiArmyPlanner.Policy&&!armyPlanner.Active).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,"Army mission released.");}
            budget.ObserveBank(current.Credits);
            budget.Expire(snapshot.Tick,entry=>BudgetReservationValid(entry,current));
            launches.Observe(current,budget,profile);
            foreach(var item in pendingItems.Where(x=>x.PolicyName=="production"&&x.Intent.ReservationId!=null&&!launches.Contains(x.Intent.ReservationId)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,"Factory startup commitment expired or target lost.");}
            expansion.Observe(current,profile,nativeProfile);expansion.ReconcileFunding(budget,profile,authorityTick,pendingItems.Select(x=>x.Intent));
            foreach(var item in pendingItems.Where(x=>x.PolicyName=="expansion"&&!expansion.ClaimsSite(x.Action.SiteId)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,"Expansion claim released.");}
            infrastructure.Observe(current,profile,budget);
            foreach(var item in pendingItems.Where(x=>x.PolicyName=="infrastructure"&&(x.Action.Kind==PlayableCommandKind.SellBuilding||x.Action.Kind==PlayableCommandKind.BuildAt)&&!infrastructure.ClaimsSlot(x.Action.SiteId,x.Action.SlotId)).ToArray())
            {pendingItems.Remove(item);Finish(item,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Rejected,"Conversion released.");}
            openingExecutor.Observe(current,knowledge.Capture(),armyPlanner.Deployment(current,armies),nativeProfile);
            demand.Observe(current,nativeProfile);
            bool threatened=AiDefensePlanner.Threats(current,profile).Length>0;
            var earlyDefense=newEmergency?defense.Propose(current,profile,nativeProfile,armies):null;
            // A non-admissible threat must not open an early macro/scout decision.
            // Only actual survival admission can override ordinary decision cadence.
            if(snapshot.Tick-lastDecisionTick<DecisionIntervalTicks&&earlyDefense==null)return;
            lastDecisionTick=snapshot.Tick;
            var observation=current;
            if(observation.OwnerId!=OwnerId)throw new InvalidOperationException("Owner loop received a foreign projection.");
            budget.ObserveBank(observation.Credits);
            budget.Expire(snapshot.Tick,entry=>BudgetReservationValid(entry,observation));
            var strategy=midgame.Review(observation,opening,mission.Mission,nativeDeployment:armyPlanner.Deployment(observation,armies));
            var proposals=new List<AiIntent>();
            bool Available(string name)=>!pendingItems.Any(p=>p.PolicyName==name);
            void Propose(string name,PlayableAiAction candidate,Action commit,Action<PlayableAiTraceRecord> feedback,int priority=0)
            {
                if(candidate==null||!Available(name))return;
                if(name!="expansion"&&candidate.EntityIds.Any(expansion.ClaimsActor)&&priority<2)return;
                if(candidate.PlayerId!=OwnerId||candidate.Generation!=Generation||candidate.Seed!=observation.Seed||candidate.SourceIdentity!=opening.SourceIdentity||candidate.ProfileId!=observation.ProfileId||candidate.ProfileRevision!=observation.ProfileRevision||candidate.SnapshotSequence!=observation.SnapshotSequence)
                    throw new InvalidOperationException("AI policy returned an action outside its owner observation.");
                var intent=Proposal(name,candidate,observation,priority);
                if(intent==null)return;
                intent.Commit=commit;intent.Feedback=feedback;proposals.Add(intent);
            }
            // TryPlan is a legacy stateful API. Each candidate runs on a transaction fork;
            // only selected forks become live policies. Unselected RNG/pending/mission changes disappear.
            var da=earlyDefense??defense.Propose(observation,profile,nativeProfile,armies);
            Propose(AiDefensePlanner.Policy,da,()=>{defense.Commit(observation,da,armies);armyPlanner.ReconcileRegistry(armies,authorityTick,"defense preempted offensive ownership");},defense.ObserveReceipt,2);
            if(!threatened)
            {
                var armyAction=armyPlanner.Active||openingExecutor.AllowsPressure?armyPlanner.ProposeWithRecords(observation,opening.WithPhase(openingExecutor.Capture().Phase),profile,nativeProfile,armies,records):null;
                Propose(AiArmyPlanner.Policy,armyAction,()=>armyPlanner.Commit(observation,armyAction,armies),armyPlanner.ObserveReceipt);
                var a=artillery.Fork();Propose("artillery",a.TryPlan(observation,opening,strategy,productionOnly:true),()=>artillery=a,a.ObserveReceipt);
            }
            EconomyPlan=new AiEconomyPlanner(roster).Plan(observation,profile,demand,nativeProfile,budget.Available,recoveryExplorerDemand:opening.Intent.Explorer>0);
            EconomyPlan=new AiEconomyPlan(openingExecutor.MacroRequests(observation,profile,nativeProfile,EconomyPlan.Candidates),EconomyPlan.ExpansionSiteIds,EconomyPlan.ExpansionReason,EconomyPlan.Capacity);
            var ex=expansion.Fork();if(!threatened&&openingExecutor.AllowsExpansion)Propose("expansion",ex.TryPlan(observation,profile,nativeProfile),()=>expansion=ex,ex.ObserveReceipt,1);
            foreach(var candidate in infrastructure.Plan(observation,profile,nativeProfile,EconomyPlan.Capacity).Where(c=>c.Legal&&(c.Action.Kind!=PlayableCommandKind.SellBuilding||!launches.ContainsProducer(c.Action.EntityIds.Single()))))
            {if(!Available("infrastructure"))break;var ip=infrastructure.Fork();Propose("infrastructure",ip.Admit(observation,candidate.Action,budget),()=>infrastructure=ip,ip.ObserveReceipt,candidate.Priority);}
            foreach(var candidate in EconomyPlan.Candidates.Where(c=>c.Legal&&Available(c.Policy)&&!expansion.ClaimsSite(c.Action.SiteId)&&!infrastructure.ClaimsSlot(c.Action.SiteId,c.Action.SlotId)))
            {
                if(candidate.Policy=="economy")
                {var e=economy.Fork();Propose("economy",e.Admit(observation,candidate.Action),()=>economy=e,e.ObserveReceipt,candidate.Priority);}
                else
                {var pr=production.Fork();Propose("production",pr.Admit(observation,candidate.Action),()=>production=pr,pr.ObserveReceipt,launches.ReservationFor(candidate.Action)!=null?1:candidate.Priority);}
            }
            var sc=scout.Fork();
            var scoutAction=sc.Admit(observation,scoutPlanner.Plan(observation,knowledge.Capture(),armies,profile,nativeProfile,ScoutBudget));
            Propose("scout",scoutAction,()=>{scoutPlanner.Commit(observation,scoutAction,armies);scout=sc;},sc.ObserveReceipt);
            var ri=new PlayableAiResearchStrategicIntent(Generation,lastScoutTick,false,opening.Intent.Tank);
            var re=research.Fork();Propose("research",re.TryPlan(observation,readiness.Observe(observation,ri)),()=>research=re,re.ObserveReceipt);

            var free=observation.Population==null?0:Math.Max(0,observation.Population.Capacity-observation.Population.Living-observation.Population.Reserved);
            int availableActions=actionLimit-pendingItems.Count;
            if(availableActions==0&&da!=null)
            {
                // C3 survival outranks a delayed macro obligation. Release one complete
                // transaction through its ordinary terminal receipt, never exceed C9 capacity.
                var displaced=pendingItems.OrderBy(p=>p.Intent.Priority).ThenByDescending(p=>p.Ordinal).ThenBy(p=>p.PolicyName,StringComparer.Ordinal).First();
                pendingItems.Remove(displaced);Finish(displaced,0,0,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"Survival defense needs bounded pending attention.");availableActions=1;
            }
            if(availableActions==0){Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);return;}
            var selected=arbiter.Select(proposals,snapshot.Tick,observation.Credits,free,availableActions,budget,armies);
            var decision=++ordinal;int actionOrdinal=0;
            economyDecision=new AiEconomyDecision(observation,profile,nativeProfile,decision,EconomyPlan,selected,arbiter.Rejections,budget);
            foreach(var intent in selected)
            {
                if(intent.Policy!="expansion"&&intent.Action.EntityIds.Any(expansion.ClaimsActor)){expansion.Release("survival preemption");expansion.ReconcileFunding(budget,profile,authorityTick);}
                intent.Commit();var local=intent.Action;
                var identity=new AiReceiptIdentity(Generation,OwnerId,decision,++actionOrdinal);
                if(intent.Policy=="infrastructure")infrastructure.Use(intent,budget);
                else if(intent.Policy=="expansion")expansion.UseFunding(intent,budget);else launches.Use(intent,identity,budget,profile);
                if(!budget.Accept(new AiBudgetEntry(intent.Id,intent.Policy,intent.Action,intent.Credits,intent.Priority,snapshot.Tick,intent.ExpiresTick,
                    "terminal receipt / invalid target / expiry",intent.Claims,BudgetTerms,identity)))throw new InvalidOperationException("Selected intent exceeds owner budget.");
                if(intent.Policy=="expansion")expansion.BeginFunding(intent,budget,profile);else launches.Begin(intent,identity,snapshot.Tick,budget,profile,nativeProfile);
                var action=Reidentify(local,allocateAction!=null?allocateAction():checked(decision*actionLimit+actionOrdinal));
                var item=new Pending{Observation=observation,ObservationIdentity=observation.Identity,ObservationTick=observation.Tick,SnapshotSequence=observation.SnapshotSequence,Action=action,DueTick=snapshot.Tick+ObservationDelayTicks,HumanSequence=humanSequence,Receipt=intent.Feedback,Scout=intent.Policy=="scout",PolicyName=intent.Policy,Ordinal=decision,Identity=identity,LocalActionId=local.ActionId,Intent=intent};
                pendingItems.Add(item);
                Emit(item,PlayableAiDeliveryStatus.Scheduled,0,null,null,null,null,"Authority scheduled.");
                Record(new PlayableAiTraceRecord(observation.Identity,action.ActionId,item.DueTick,0,0,PlayableAiDeliveryStatus.Scheduled,null,"Authority scheduled.",OwnerId,action.SourceIdentity,identity,item.PolicyName,action.Kind));
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
        private static bool SameOrder(PlayableAiAction a,PlayableEntitySnapshot u)=>(u.Queue?.Pending==null&&(u.Queue==null||u.Queue.Deferred.Count==0))&&(a.Kind==PlayableCommandKind.Stop?u.CurrentOrder==null&&!u.Held:
            u.CurrentOrder!=null&&(a.Kind==PlayableCommandKind.Follow?u.CurrentOrder.Kind==PlayableTacticalOrderKind.Follow&&u.CurrentOrder.TargetId==a.TargetId:
            a.Kind==PlayableCommandKind.Attack?u.CurrentOrder.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==a.TargetId:
            u.CurrentOrder.Kind==(a.Kind==PlayableCommandKind.Move?PlayableTacticalOrderKind.Move:PlayableTacticalOrderKind.AttackMove)&&u.CurrentOrder.Destination.Equals(a.Target)));
        private int BudgetCost(PlayableAiAction action)
        {
            if(action.Kind==PlayableCommandKind.BuildAt)return TerritoryRules.Cost(profile,action.BuildingKind);
            if(action.Kind==PlayableCommandKind.QueueTank||action.Kind==PlayableCommandKind.QueueExplorer||action.Kind==PlayableCommandKind.QueueShkval)
                return (int)roster.CreditCost(roster.Production(action.Kind).kind,profile);
            if(action.Kind==PlayableCommandKind.QueueResearch)return (int)Math.Ceiling(action.ResearchKind==PlayableResearchKind.TankChassis?profile.TankChassisCost:action.ResearchKind==PlayableResearchKind.ExplorerAssaultGuns?profile.ExplorerAssaultCost:profile.ShkvalGuidanceCost);
            return 0;
        }
        private bool BudgetReservationValid(AiBudgetEntry entry,PlayableAiObservation o)=>AiExpansionPlanner.IsFunding(entry)?expansion.FundTargetValid(entry,o):BudgetTargetValid(entry.Action,o);
        private static bool BudgetTargetValid(PlayableAiAction action,PlayableAiObservation observation)=>
            action.EntityIds.All(id=>observation.Entities.Any(x=>x.Id==id&&x.Owner==observation.Owner&&x.Health>0)||observation.Buildings.Any(x=>x.Id==id&&x.Owner==observation.Owner&&x.Health>0))&&
            (action.Kind!=PlayableCommandKind.BuildAt||observation.Sites.Any(x=>x.Site.Id==action.SiteId&&(x.Owner==observation.Owner||action.SlotId==0&&x.Claimant==observation.Owner&&x.Progress>=1))&&
                (action.ParentId==0||observation.Buildings.Any(x=>x.Id==action.ParentId&&x.Owner==observation.Owner&&x.Health>0)));
        private AiIntent Proposal(string policyName,PlayableAiAction action,PlayableAiObservation o,int priority)
        {
            if(AiEconomyAdmission.Reject(o,profile,action,catalog:roster)!=null)return null;
            int cost=0,pop=0;var claims=action.EntityIds.Select(id=>"recipient:"+id).ToList();
            // Wire v2 has one pending callback per policy. Alternative candidates compete
            // for that policy as well as their physical recipients/slots.
            claims.Add("policy:"+policyName);
            if(policyName=="expansion")
            claims.Add("expansion-site:"+action.SiteId);
            bool queue=action.Kind==PlayableCommandKind.QueueTank||action.Kind==PlayableCommandKind.QueueShkval||action.Kind==PlayableCommandKind.QueueExplorer;
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
                if(site==null||site.Contested||o.Buildings.Any(b=>b.SiteId==action.SiteId&&b.SlotId==action.SlotId))return null;
            }
            if(action.Kind==PlayableCommandKind.QueueResearch)
            {
                var researchCost=o.ResearchAvailability.SingleOrDefault(x=>x.Kind==action.ResearchKind);
                if(researchCost==null||!researchCost.Available)return null;cost=(int)Math.Ceiling(researchCost.Cost);
            }
            if(action.EntityIds.Any(id=>!o.Entities.Any(u=>u.Id==id&&u.Owner==o.Owner&&u.Health>0)&&!o.Buildings.Any(b=>b.Id==id&&b.Owner==o.Owner&&b.Health>0)))return null;
            // Same effective tactical order is a no-op, even after cooldown. A changed order must
            // also respect the profile recipient interval (survival commands may preempt it).
            if(Tactical(action))
            {
                var units=o.Entities.Where(u=>action.EntityIds.Contains(u.Id)).ToArray();
                if(units.Length==0||units.All(u=>SameOrder(action,u)))return null;
                // Attention belongs to the durable group, not the selected subset. A different
                // member cannot bypass its army's cooldown; survival may still preempt it.
                var groupIds=new HashSet<int>(action.EntityIds);
                var armyIds=new HashSet<long>(action.EntityIds.Select(armies.ArmyFor).Where(id=>id!=0));
                foreach(var a in armies.Capture().Armies.Where(a=>armyIds.Contains(a.Id)))groupIds.UnionWith(a.Members);
                if(priority<2&&o.Entities.Any(u=>groupIds.Contains(u.Id)&&RecentGroupOrder(o,u)))return null;
            }
            string key=AiEconomyAdmission.IntentId(policyName,action);
            // Relevant admission condition changes reopen bounded retries; tick, snapshot and
            // unrelated income do not reset a recipient/placement rejection.
            string conditions=(o.Credits>=cost)+":"+(o.Population!=null&&Math.Max(0,o.Population.Capacity-o.Population.Living-o.Population.Reserved)>=pop?1:0)+":"+
                string.Join(",",o.Buildings.Where(b=>b.SiteId==action.SiteId||action.EntityIds.Contains(b.Id)).OrderBy(b=>b.Id).Select(b=>b.Id+"/"+b.Phase+"/"+(b.PrivateState?.QueueCount??0)+(policyName=="infrastructure"?"/"+b.Health+"/"+b.PrivateState?.Lifecycle?.RepairBlockedReason+"/"+b.PrivateState?.Lifecycle?.SaleBlockedReason:"")))+":"+
                string.Join(",",o.Entities.Where(u=>action.EntityIds.Contains(u.Id)||u.Id==action.TargetId).OrderBy(u=>u.Id).Select(u=>u.Id+"/"+u.CurrentOrder?.Kind+"/"+u.CurrentOrder?.TargetId));
            long startup=policyName=="expansion"&&action.Kind==PlayableCommandKind.Move&&!expansion.HasFundingFor(action,budget)?(long)TerritoryRules.Cost(profile,action.BuildingKind):action.Kind==PlayableCommandKind.BuildAt&&action.BuildingKind==PlayableBuildingKind.Factory&&o.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory)?(long)nativeProfile.Value("economy.factoryLaunchCycles")*PlayableUnitRules.Cost(profile,PlayableEntityKind.Tank):0;
            return new AiIntent(key,policyName,action,checked(o.Tick+DecisionIntervalTicks+ObservationDelayTicks),cost,pop,claims,priority,1,conditions,startupFund:startup,reservationId:policyName=="infrastructure"?infrastructure.ReservationFor(action,budget):policyName=="expansion"?expansion.ReservationFor(action,budget):launches.ReservationFor(action),overdue:policyName=="production"&&priority==1&&launches.ReservationFor(action)==null||policyName==AiArmyPlanner.Policy&&o.Tick-armyPlanner.Capture().LastInitiativeTick>=AiProfile.SecondsToTicks(nativeProfile.Value("armies.offensiveOverdueSeconds"),30));
        }
        private static PlayableAiAction Reidentify(PlayableAiAction a,long id)=>new PlayableAiAction(id,a.PlayerId,a.ProfileId,a.ProfileRevision,a.Generation,a.SnapshotSequence,a.Kind,a.CopyEntityIds(),a.Target,a.SiteId,a.SlotId,a.BuildingKind,a.ParentId,a.TargetId,a.ProductionOrderId,a.UnitKind,a.ResearchKind,a.Seed,a.SourceIdentity);
        internal void Stop(long? tick=null){if(tick.HasValue)authorityTick=tick.Value;while(pending!=null)Cancel(PlayableAiDeliveryStatus.Stopped,"Runtime stopped.");budget.ReleaseAllReservations();launches.Clear(budget);expansion.Release("runtime stopped");infrastructure.Release("runtime stopped");armies.DisbandAll();armyPlanner.Stop();defense.Stop();Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);}
        private void Cancel(PlayableAiDeliveryStatus status,string message)
        {var item=pending;pendingItems.Remove(item);Finish(item,0,0,status,status==PlayableAiDeliveryStatus.Stopped?PlayableCommandStatus.Stopped:PlayableCommandStatus.Cancelled,message);}
        private void Finish(Pending item,long sequence,long applicationTick,PlayableAiDeliveryStatus status,PlayableCommandStatus runtimeStatus,string message,bool gameplayPaid=true)
        {
            var record=new PlayableAiTraceRecord(item.ObservationIdentity,item.Action.ActionId,item.DueTick,sequence,
                applicationTick,status,runtimeStatus,message,OwnerId,item.Action.SourceIdentity,item.Identity,item.PolicyName,item.Action.Kind);
            Emit(item,status,sequence,sequence!=0||status==PlayableAiDeliveryStatus.Stale?(long?)authorityTick:null,authorityTick,status==PlayableAiDeliveryStatus.Applied?(long?)applicationTick:null,runtimeStatus,message);
            lastPolicy=item.PolicyName;lastActionKind=item.Action.Kind;
            arbiter.Terminal(item.Intent,authorityTick,status);
            budget.Reconcile(item.Intent.Id,item.Identity,status,gameplayPaid);
            launches.Terminal(item.Intent,item.Identity,status,gameplayPaid,budget,profile);
            var terminalReceipt=new PlayableAiTraceRecord(record.ObservationIdentity,item.LocalActionId,record.DueTick,sequence,applicationTick,status,runtimeStatus,message,OwnerId,item.Action.SourceIdentity,item.Identity,item.PolicyName,item.Action.Kind);item.Receipt(terminalReceipt);openingExecutor.ObserveReceipt(item.Action,terminalReceipt);if(item.PolicyName=="expansion")expansion.TerminalFunding(item.Intent,status,budget,profile);expansion.ReconcileFunding(budget,profile,authorityTick,pendingItems.Select(x=>x.Intent));Record(record);
#if DEVELOPMENT_BUILD
            Console.WriteLine("[Spacewars owner AI] owner="+OwnerId+" source="+PlayableAiOpeningComposition.SourceIdentity+" profile="+profile.ProfileId+"@"+profile.Revision+
                " seed="+opening.MatchSeed+" policy="+item.PolicyName+" kind="+item.Action.Kind+" status="+status+" generation="+Generation+
                " observation="+item.ObservationIdentity+" action="+item.Action.ActionId+" due="+item.DueTick+" sequence="+sequence+" tick="+applicationTick+
                " commandStatus="+runtimeStatus+" decisionOrdinal="+item.Ordinal+" observationTick="+item.Observation.Tick+
                " actionTick="+item.Observation.Tick+" receiptTick="+authorityTick+" applicationTick="+
                (status==PlayableAiDeliveryStatus.Applied?applicationTick.ToString(System.Globalization.CultureInfo.InvariantCulture):"none"));
#endif
        }
        private void Emit(Pending item,PlayableAiDeliveryStatus status,long sequence,long? delivery,long? receipt,long? application,PlayableCommandStatus? runtimeStatus,string message)
        {if(item.Observation!=null)audit?.Invoke(new PlayableAiEvaluationEvent(item.Observation,item.Action,item.PolicyName,item.Ordinal,item.DueTick,status,sequence,delivery,receipt,application,runtimeStatus,message));}
        private void Record(PlayableAiTraceRecord record)
        {records.Add(record);if(records.Count>128)records.RemoveAt(0);Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);}
        private PlayableAiOwnerCheckpoint Capture()=>new PlayableAiOwnerCheckpoint(OwnerId,PlayableAiOpeningComposition.SourceIdentity,profile.ProfileId,profile.Revision,opening.MatchSeed,Generation,lastDecisionTick,lastScoutTick,
            pending?.Action.ActionId??0,pending?.DueTick??0,lastCommandSequence,lastPolicy,lastActionKind,opening,mission.Mission,midgame.Capture(),artillery.Capture(),records,economyDecision,lastScoutOrderTick).BindExpansion(expansion.Capture()).BindInfrastructure(infrastructure.Capture()).BindArmies(armies.Capture()).BindArmyMission(armyPlanner.Capture()).BindKnowledge(knowledge.Capture()).BindOpeningExecution(openingExecutor.Capture());
    }
}

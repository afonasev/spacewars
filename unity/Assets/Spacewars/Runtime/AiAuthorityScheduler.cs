using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // The ordinary Player, roster runtime and later headless host share this authority path.
    internal sealed partial class AiAuthorityScheduler
    {
        private readonly PlayableAiOwnerLoop[] owners;
        private readonly bool performanceTrace=Array.IndexOf(Environment.GetCommandLineArgs(),"-routePerformanceEvidence")>=0;
        private long nextActionId;
        private readonly object allocatorGate=new object();
        private long AllocateAction(){lock(allocatorGate)return checked(++nextActionId);}
        private bool backgroundReviews,stopped;
        private readonly object reviewGate=new object();
        private Action queuedReview;
        private Thread reviewThread;
        private bool holdReviewForCapture;
        internal bool HoldReviewForCapture
        {
            get=>holdReviewForCapture;
            set
            {
                holdReviewForCapture=value;
                if(value)retryReviewOwner=-1;
            }
        }
        internal Action<string,long,long,string,AiReviewTiming> ReviewTelemetry;
        private int nextReviewOwner,retryReviewOwner=-1;
        private readonly HashSet<string> relayShareDue=new HashSet<string>();
        private ReviewWork reviewWork;
        private sealed class ReviewWork
        {
            internal int Index;internal long Human;internal PlayableSnapshot Snapshot;
            internal AiBudgetState Initial;internal System.Threading.Tasks.Task<PlayableAiOwnerLoop> Task;
            internal bool Invalidated,Retry;
            internal long ReportedAge;
            internal long CaptureAt, QueuedAt, StartedAt, RouteStart, RouteEnd, FinishedAt;
            internal PlayableDomain.NuclearEscapePlan[] Evasions;
            internal (int Site,int Recipient)[] RelayTransfers;
        }
        internal int NextReviewOwnerForCapture=>nextReviewOwner;
        internal void RestoreReviewCursor(int cursor)
        {if(cursor<0||cursor>=Math.Max(1,owners.Length))throw new ArgumentException("Invalid next AI review owner.");nextReviewOwner=cursor;}
        internal void EnableBackgroundReviews()
        {
            if(backgroundReviews || owners.Length==0)return;
            backgroundReviews=true;
            reviewThread=new Thread(RunReviews){IsBackground=true,Name="Spacewars.AI.Decisions"};
            reviewThread.Start();
        }
        private void RunReviews()
        {
            while(true){
                Action calculate;
                lock(reviewGate){
                    while(queuedReview==null&&!stopped)Monitor.Wait(reviewGate);
                    if(queuedReview==null)return;
                    calculate=queuedReview;
                    queuedReview=null;
                }
                calculate();
            }
        }
        internal bool ReviewIdle=>Volatile.Read(ref reviewWork)?.Task.IsCompleted??true;
        internal bool ReviewWorkerStopped=>!(Volatile.Read(ref reviewThread)?.IsAlive??false);
        private void InvalidateReview()
        {
            retryReviewOwner=-1;
            if(reviewWork!=null)reviewWork.Invalidated=true;
        }
        private bool Reviewing(PlayableAiOwnerLoop owner)=>reviewWork!=null&&owners[reviewWork.Index]==owner&&!reviewWork.Invalidated;
        private void CompleteReview(PlayableDomain domain,Func<string,long> human,bool paused)
        {
            var work=reviewWork;if(work==null)return;
            var original=owners[work.Index];
            if(paused||stopped||!domain.Authorizes(original.OwnerId)||human(original.OwnerId)!=work.Human)work.Invalidated=true;
            if(!work.Task.IsCompleted){
                long age=domain.Tick-work.Snapshot.Tick;
                if(age>0 && (work.ReportedAge==0||age-work.ReportedAge>=work.ReportedAge)){
                    work.ReportedAge=age;
                    ReviewTelemetry?.Invoke(original.OwnerId,work.Snapshot.Tick,domain.Tick,"pending",default);
                }
                return;
            }
            long polledAt = Stopwatch.GetTimestamp();
            reviewWork=null;
            string disposition="failed";
            try {
                var candidate=work.Task.GetAwaiter().GetResult();
                disposition="discarded";
                if(work.Invalidated)return;
                if(work.RelayTransfers!=null)domain.ApplyRelaySharing(domain.OwnerFor(original.OwnerId),work.RelayTransfers);
                if(work.Evasions!=null)foreach(var plan in work.Evasions)domain.ApplyNuclearPlan(original.OwnerId,plan);
                if(!work.Retry&&!HoldReviewForCapture&&candidate.NewReviewIntentsExpired(work.Snapshot.Tick,domain.Tick))
                {
                    // Refresh the same owner's observation once while its path answers
                    // are warm. Never publish proposals that cannot reach delivery.
                    retryReviewOwner=work.Index;
                    disposition="expired";
                    return;
                }
                if(candidate.MissedCombatRelease(original,work.Snapshot.Tick))return;
                original.ReconcileBudget(domain);
                original.ReconcileReviewClocks(domain.Tick);
                if(!candidate.MergeReviewBudget(work.Initial,original.Budget.Capture(),domain.Tick))return;
                candidate.InheritCombatAttention(original);
                candidate.ReconcileReviewMembership(domain,domain.OwnerFor(original.OwnerId));
                candidate.ValidateBudget(domain);
                candidate.AdoptExecutionAuthority(original,domain);
                candidate.AdoptCheckpointPublication(original);
                owners[work.Index]=candidate;candidate.BindActionAllocator(AllocateAction);
                communicationNarrator.Observe(work.Snapshot,candidate.Checkpoint,domain.RoutingProfile);
                domain.RecordAiState(candidate.Checkpoint);
                System.Threading.Volatile.Write(ref communications,communicationNarrator.Snapshot);
                System.Threading.Volatile.Write(ref communicationDiagnostics,communicationNarrator.Diagnostics);
                disposition="adopted";
            } finally {
                ReviewTelemetry?.Invoke(original.OwnerId,work.Snapshot.Tick,domain.Tick,disposition,
                    new AiReviewTiming(work.CaptureAt, work.QueuedAt, work.StartedAt, work.RouteStart, work.RouteEnd,
                        work.FinishedAt, polledAt, Stopwatch.GetTimestamp()));
            }
        }
        internal void SettleReviewForCapture(PlayableDomain domain,Func<string,long> human,bool paused)
        {
            CompleteReview(domain,human,paused);
        }
        internal PlayableAiOwnerCheckpoint CheckpointFor(string id)=>owners.FirstOrDefault(o=>o.OwnerId==id)?.Checkpoint;
        private readonly AiCommunicationNarrator communicationNarrator=new AiCommunicationNarrator();
        private AiCommunication[] communications=Array.Empty<AiCommunication>();
        private string[] communicationDiagnostics=Array.Empty<string>();
        internal string[] CommunicationDiagnostics=>System.Threading.Volatile.Read(ref communicationDiagnostics);
        internal AiCommunication[] Communications=>System.Threading.Volatile.Read(ref communications);

        internal AiAuthorityScheduler(IEnumerable<PlayableAiOwnerLoop> loops)
        {
            owners=loops.Where(x=>x!=null).OrderBy(x=>x.OwnerId,StringComparer.Ordinal).ToArray();
            if(owners.Select(x=>x.OwnerId).Distinct(StringComparer.Ordinal).Count()!=owners.Length)throw new ArgumentException("Duplicate AI owner.");
            foreach(var owner in owners)owner.BindActionAllocator(AllocateAction);
        }
        internal IReadOnlyList<PlayableAiOwnerLoop> Owners=>Array.AsReadOnly(owners);
        internal double IncomeMultiplier(string ownerId)
        {
            foreach(var owner in owners)
                if(owner.OwnerId==ownerId)return owner.IncomeMultiplier;
            return 1;
        }
        internal void Deliver(PlayableDomain domain,Func<string,long> human,bool paused)
        {
            foreach(var owner in owners)owner.BindExecutionDomain(domain);
            domain.BindCombatMotionAuthority(()=>owners);
            CompleteReview(domain,human,paused);
            using var measured=PerformanceProfile.Measure(ProfileStage.Delivery);
            bool awaitingRouteBoundary=false;
            try{
                foreach(var owner in owners){
                    owner.BindExecutionDomain(domain);
                    if(!paused)domain.UpdateNuclearEvasion(owner.OwnerId,owner.Difficulty);
                    if(Reviewing(owner)){owner.ReconcileBudget(domain);continue;}
                    bool ready=true;
                    if(!paused){
                        if(!domain.BackgroundDecisionProjections)owner.InitializeOpeningProtection(domain);
                        if(!domain.BackgroundDecisionProjections||owner.NeedsDeliveryCheck(domain.Tick))
                            ready=owner.GuardDefenseReadiness(domain);
                    }
                    awaitingRouteBoundary|=owner.DeliverReady(domain,human(owner.OwnerId),paused,ready);
                }
            }finally{
                if(!awaitingRouteBoundary)domain.Navigation.RoutePort.CancelCommandPreparation(scheduledAi:true);
            }
            domain.BindCombatMotionAuthority(()=>owners);
        }
        internal bool? AiProductionRepeat(PlayableDomain domain,PlayableOwner owner,int producerId,PlayableEntityKind kind)
        {
            string id=domain.FinancialOwnerId(owner);
            foreach(var loop in owners)
                if(loop.OwnerId==id)return loop.RepeatProductionAdmission(domain,producerId,kind);
            return true;
        }
        internal void ShareRelays(PlayableDomain domain)
        {
            foreach(var owner in owners)
                if(owner.RelayShareDue(domain.Tick)){
                    if(domain.BackgroundDecisionProjections)relayShareDue.Add(owner.OwnerId);
                    else domain.ShareAiRelays(owner,owners);
                }
        }
        internal void Review(PlayableDomain domain,long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,int seed,Func<string,long> human)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Ai);
            // Human-only matches have no publications, assignments or pending
            // reviews. Avoid constructing a default AI profile and empty team
            // board on every authority tick (including paused publications).
            if(owners.Length==0)return;
            bool hadReview=reviewWork!=null;
            CompleteReview(domain,human,paused);
            // A result settling after delivery must leave the next delivery phase
            // free. Replacing it here can keep that owner Reviewing at every
            // delivery forever, until all of its otherwise timely intents expire.
            if(backgroundReviews&&hadReview&&reviewWork==null)return;
            if(backgroundReviews&&owners.Length>0){for(int i=0;i<owners.Length&&!domain.Authorizes(owners[nextReviewOwner].OwnerId);i++)nextReviewOwner=(nextReviewOwner+1)%owners.Length;}
            if(backgroundReviews){ReviewBackground(domain,sequence,status,paused,metrics,seed,human);return;}
            // Freeze all owner projections/publications at the phase barrier. Review
            // cannot expose an earlier owner's newly committed intentions to a later one.
            var snapshots=new Dictionary<string,PlayableSnapshot>(StringComparer.Ordinal);
            var projectionTimes=new Dictionary<string,long>(StringComparer.Ordinal);
            var teamSnapshots=new Dictionary<string,PlayableSnapshot>(StringComparer.Ordinal);
            foreach(var owner in owners.Where(o=>domain.Authorizes(o.OwnerId)))
            {
                using var projectionScope=PerformanceProfile.Measure(ProfileStage.AiProjection);
                long projectionStart=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                try{
                var view=domain.PlayerSnapshotForAiReview(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId),!paused&&owner.NeedsArmyRoutes(domain.Tick),owner.ArmyState,owner.NeedsTerritorialRoutes,owner.NeedsVictorySearch,
                    owner.RejectedDominationApproaches(domain.Tick),owner.KnownVictoryOutposts);
                if(!paused&&!owner.NeedsArmyRoutes(domain.Tick)&&owner.NeedsDefenseRoutes(view))view=domain.PlayerSnapshotForAiReview(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId),true,owner.ArmyState,owner.NeedsTerritorialRoutes,owner.NeedsVictorySearch,
                    owner.RejectedDominationApproaches(domain.Tick),owner.KnownVictoryOutposts);
                snapshots.Add(owner.OwnerId,view);teamSnapshots.Add(owner.OwnerId,view);
                if(performanceTrace)projectionTimes.Add(owner.OwnerId,System.Diagnostics.Stopwatch.GetTimestamp()-projectionStart);
                }catch(ObservedPathPending){
                    // Team coordination still sees all owners at this same world phase.
                    // This lightweight view performs no observed route solve.
                    teamSnapshots.Add(owner.OwnerId,domain.PlayerSnapshot(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId)));
                }
            }
            var allied=owners.Where(o=>teamSnapshots.TryGetValue(o.OwnerId,out var view)&&view.Participants.Any(p=>p.Id!=o.OwnerId&&p.Control==OfflineControl.Ai&&p.Team==view.Team&&teamSnapshots.ContainsKey(p.Id))).ToArray();
            System.Collections.Generic.IReadOnlyDictionary<string,AiTeamAssignment> board;
            using(PerformanceProfile.Measure(ProfileStage.TeamBoard))board=AiTeamCoordinator.Resolve(allied.Select(o=>o.PublishTeam(PlayableAiObservation.From(teamSnapshots[o.OwnerId]))),owners.FirstOrDefault()?.TeamProfile??Spacewars.Simulation.Ai.AiProfile.Initial);
            foreach(var owner in owners)owner.BindTeamAssignment(board.TryGetValue(owner.OwnerId,out var assignment)?assignment:null);
            foreach(var owner in owners)
            {
                long begin=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                using(PerformanceProfile.Measure(ProfileStage.AiBudget))owner.ReconcileBudget(domain);
                if(!domain.Authorizes(owner.OwnerId)){owner.Stop(domain.Tick);continue;}
                long budget=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                if(!snapshots.TryGetValue(owner.OwnerId,out var snapshot))continue;
                long projection=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
#if UNITY_INCLUDE_TESTS
                if(!paused)domain.BeforeOwnerReviewForTests?.Invoke();
#endif
                owner.Review(snapshot,human(owner.OwnerId));
                communicationNarrator.Observe(snapshot,owner.Checkpoint,domain.RoutingProfile);
                long review=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                using(PerformanceProfile.Measure(ProfileStage.AiRecord))domain.RecordAiState(owner.Checkpoint);
                if(performanceTrace){
                    long end=System.Diagnostics.Stopwatch.GetTimestamp();double scale=1000d/System.Diagnostics.Stopwatch.Frequency;
                    if((end-begin+projectionTimes[owner.OwnerId])*scale>=20)Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "AI_STAGE tick={0} owner={1} budget_ms={2:F4} projection_ms={3:F4} review_ms={4:F4} record_ms={5:F4}",
                        domain.Tick,owner.OwnerId,(budget-begin)*scale,projectionTimes[owner.OwnerId]*scale,(review-projection)*scale,(end-review)*scale));
                }
            }
            System.Threading.Volatile.Write(ref communications,communicationNarrator.Snapshot);
            System.Threading.Volatile.Write(ref communicationDiagnostics,communicationNarrator.Diagnostics);
        }
        private void ReviewBackground(PlayableDomain domain,long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,int seed,Func<string,long> human)
        {
            foreach(var owner in owners){using(PerformanceProfile.Measure(ProfileStage.AiBudget))owner.ReconcileBudget(domain);if(!domain.Authorizes(owner.OwnerId))owner.Stop(domain.Tick);}
            // An in-flight decision already owns its immutable phase. Rebuilding
            // every owner's expensive route projection cannot improve that input.
            if(paused||stopped||HoldReviewForCapture||reviewWork!=null||owners.Length==0)return;
            bool retry=retryReviewOwner>=0&&domain.Authorizes(owners[retryReviewOwner].OwnerId);
            int index=retry?retryReviewOwner:nextReviewOwner;
            retryReviewOwner=-1;
            var selected=owners[index];
            if(!domain.Authorizes(selected.OwnerId))return;
            // No domain mutation occurs during these synchronous team projections.
            // The async review receives immutable DTOs after the scope is closed.
            long captureAt = Stopwatch.GetTimestamp();
            using var projectionBatch=domain.BeginProjectionBatch();
            PlayableSnapshot snapshot;
            Func<PlayableAiOwnerLoop,PlayableSnapshot> routes=null;
            using(PerformanceProfile.Measure(ProfileStage.AiProjection)){
                var owner=domain.OwnerFor(selected.OwnerId);
                if(domain.BackgroundDecisionProjections){
                    snapshot=domain.CaptureDecisionView(sequence,status,metrics,seed,owner);
                    routes=domain.CaptureDecisionRoutes(snapshot);
                }else try{
                    snapshot=domain.PlayerSnapshotForAiReview(sequence,status,false,metrics,null,seed,owner,selected.NeedsArmyRoutes(domain.Tick),selected.ArmyState,selected.NeedsTerritorialRoutes,selected.NeedsVictorySearch,
                        selected.RejectedDominationApproaches(domain.Tick),selected.KnownVictoryOutposts);
                    if(!selected.NeedsArmyRoutes(domain.Tick)&&selected.NeedsDefenseRoutes(snapshot))
                        snapshot=domain.PlayerSnapshotForAiReview(sequence,status,false,metrics,null,seed,owner,true,selected.ArmyState,selected.NeedsTerritorialRoutes,selected.NeedsVictorySearch,
                            selected.RejectedDominationApproaches(domain.Tick),selected.KnownVictoryOutposts);
                }catch(ObservedPathPending){if(!retry)nextReviewOwner=(index+1)%owners.Length;return;}
            }
            var relay=relayShareDue.Remove(selected.OwnerId)?domain.CaptureRelaySharing(selected,owners):null;
            var evasion=domain.BackgroundDecisionProjections?domain.CaptureNuclearPlans(snapshot,selected.Difficulty):null;
            var artillery=domain.BackgroundDecisionProjections?domain.CaptureReviewArtillery(snapshot,selected.ArmyState):null;
            var input=selected.CaptureReviewInput(domain.Tick);
            // Only this team's publications can influence this owner's assignment.
            // All views freeze at this phase; worker code sees detached owner copies.
            var team=owners.Where(o=>domain.Authorizes(o.OwnerId)&&snapshot.Participants.Any(p=>p.Id==o.OwnerId&&p.Control==OfflineControl.Ai&&p.Team==snapshot.Team)).ToArray();
            var teamInputs=team.Length>1?team.Select(o=>ReferenceEquals(o,selected)?input:o.CaptureReviewInput(domain.Tick)).ToArray():Array.Empty<PlayableAiOwnerLoop.ReviewCloneInput>();
            var teamViews=teamInputs.Length==0?Array.Empty<AiTeamPublicationObservation>():team.Select(o=>o.OwnerId==selected.OwnerId?null:domain.TeamPublicationObservation(sequence,status,metrics,seed,domain.OwnerFor(o.OwnerId))).ToArray();
            var work=new ReviewWork{Index=index,Human=human(selected.OwnerId),Snapshot=snapshot,Initial=selected.Budget.Capture(),Retry=retry,CaptureAt=captureAt};
#if UNITY_INCLUDE_TESTS
            var beforeReview=domain.BeforeOwnerReviewForTests;
#endif
            var completion=new TaskCompletionSource<PlayableAiOwnerLoop>(TaskCreationOptions.RunContinuationsAsynchronously);
            work.Task=completion.Task;
            void Calculate()
            {
                work.StartedAt=Stopwatch.GetTimestamp();
                try {
                    var clone=input.Restore();clone.BindActionAllocator(AllocateAction);
                    if (routes != null)
                    {
                        work.RouteStart = Stopwatch.GetTimestamp();
                        snapshot = routes(clone);
                        work.RouteEnd = Stopwatch.GetTimestamp();
                    }
                    work.RelayTransfers=relay?.Invoke();
                    work.Evasions=evasion?.Invoke();
                    if(artillery!=null)snapshot=snapshot.WithDecisionArtillery(artillery());
                    var teamOwners=teamInputs.Select(i=>ReferenceEquals(i,input)?clone:i.Restore()).ToArray();
                    System.Collections.Generic.IReadOnlyDictionary<string,AiTeamAssignment> board;
                    using(PerformanceProfile.Measure(ProfileStage.TeamBoard))board=AiTeamCoordinator.Resolve(teamOwners.Select((o,i)=>teamViews[i]==null?o.PublishTeam(PlayableAiObservation.From(snapshot)):o.PublishTeam(teamViews[i])),clone.TeamProfile);
                    clone.BindTeamAssignment(board.TryGetValue(clone.OwnerId,out var assignment)?assignment:null);
#if UNITY_INCLUDE_TESTS
                    beforeReview?.Invoke();
#endif
                    clone.Review(snapshot,work.Human);
                    clone.PrepareProductionRepeat(snapshot);
                    work.FinishedAt=Stopwatch.GetTimestamp();
                    completion.SetResult(clone);
                } catch(Exception error){work.FinishedAt=Stopwatch.GetTimestamp();completion.SetException(error);}
            }
            lock(reviewGate){
                if(stopped||queuedReview!=null)throw new InvalidOperationException("AI worker cannot accept another review.");
                work.QueuedAt = Stopwatch.GetTimestamp();
                queuedReview=Calculate;
                Monitor.Pulse(reviewGate);
            }
            work.Task.ContinueWith(t=>{var observed=t.Exception;},System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            reviewWork=work;if(!retry)nextReviewOwner=(index+1)%owners.Length;
            System.Threading.Volatile.Write(ref communications,communicationNarrator.Snapshot);
            System.Threading.Volatile.Write(ref communicationDiagnostics,communicationNarrator.Diagnostics);
        }
        internal void PrepareNavigationViews(PlayableDomain domain,int seed,Func<bool> stopping=null)
        {
            foreach(var owner in owners){
                if(stopping?.Invoke()==true)break;
                if(domain.Authorizes(owner.OwnerId))domain.PreparePublicNavigation(domain.OwnerFor(owner.OwnerId),stopping);
            }
        }
        internal void Rebind(PlayableProfile profile){InvalidateReview();foreach(var owner in owners)owner.Rebind(profile);}
        internal void Stop(long tick)
        {
            lock(reviewGate){if(stopped)return;stopped=true;Monitor.Pulse(reviewGate);}
            if(reviewWork!=null)
                ReviewTelemetry?.Invoke(owners[reviewWork.Index].OwnerId,reviewWork.Snapshot.Tick,tick,"stopped",default);
            InvalidateReview();
            foreach(var owner in owners)owner.Stop(tick);
        }
    }
}

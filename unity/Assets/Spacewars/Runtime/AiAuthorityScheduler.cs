using System;
using System.Collections.Generic;
using System.Linq;
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
        private int nextReviewOwner;
        private ReviewWork reviewWork;
        private sealed class ReviewWork
        {
            internal int Index;internal long Human;internal PlayableSnapshot Snapshot;
            internal AiBudgetState Initial;internal System.Threading.Tasks.Task<PlayableAiOwnerLoop> Task;
            internal bool Invalidated;
        }
        internal int NextReviewOwnerForCapture=>nextReviewOwner;
        internal void RestoreReviewCursor(int cursor)
        {if(cursor<0||cursor>=Math.Max(1,owners.Length))throw new ArgumentException("Invalid next AI review owner.");nextReviewOwner=cursor;}
        internal void EnableBackgroundReviews(){backgroundReviews=true;}
        internal bool ReviewWorkerStopped=>System.Threading.Volatile.Read(ref reviewWork)?.Task.IsCompleted??true;
        private void InvalidateReview(){if(reviewWork!=null)reviewWork.Invalidated=true;}
        private bool Reviewing(PlayableAiOwnerLoop owner)=>reviewWork!=null&&owners[reviewWork.Index]==owner&&!reviewWork.Invalidated;
        private void CompleteReview(PlayableDomain domain,Func<string,long> human,bool paused)
        {
            var work=reviewWork;if(work==null)return;
            var original=owners[work.Index];
            if(paused||stopped||!domain.Authorizes(original.OwnerId)||human(original.OwnerId)!=work.Human)work.Invalidated=true;
            if(!work.Task.IsCompleted)return;
            reviewWork=null;
            var candidate=work.Task.GetAwaiter().GetResult();
            if(work.Invalidated)return;
            original.ReconcileBudget(domain);
            original.ReconcileReviewClocks(domain.Tick);
            if(!candidate.MergeReviewBudget(work.Initial,original.Budget.Capture(),domain.Tick))return;
            candidate.InheritCombatAttention(original);
            var live=domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,work.Snapshot.Seed,domain.OwnerFor(original.OwnerId));
            candidate.ReconcileReviewMembership(live);
            candidate.ValidateBudget(domain);
            candidate.AdoptExecutionAuthority(original,domain);
            owners[work.Index]=candidate;candidate.BindActionAllocator(AllocateAction);
            communicationNarrator.Observe(work.Snapshot,candidate.Checkpoint,domain.RoutingProfile);
            domain.RecordAiState(candidate.Checkpoint);
            System.Threading.Volatile.Write(ref communications,communicationNarrator.Snapshot);
            System.Threading.Volatile.Write(ref communicationDiagnostics,communicationNarrator.Diagnostics);
        }
        internal void DrainReviewsForCapture(PlayableDomain domain,Func<string,long> human,Func<bool> stopping)
        {
            while(reviewWork!=null){
                if(stopping?.Invoke()==true)throw new OperationCanceledException("AI review capture stopped.");
                CompleteReview(domain,human,false);
                if(reviewWork!=null)System.Threading.Thread.Sleep(1);
            }
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
        internal void Deliver(PlayableDomain domain,Func<string,long> human,bool paused)
        {foreach(var current in owners)current.BindExecutionDomain(domain);domain.BindCombatMotionAuthority(()=>owners);CompleteReview(domain,human,paused);domain.ProtectedAiCapturer=owner=>owners.FirstOrDefault(o=>o.OwnerId==domain.FinancialOwnerId(owner))?.ProtectedCapturerId??0;using var measured=PerformanceProfile.Measure(ProfileStage.Delivery);foreach(var owner in owners){owner.BindExecutionDomain(domain);if(Reviewing(owner)){owner.ReconcileBudget(domain);continue;}bool ready=true;if(!paused){owner.InitializeOpeningProtection(domain);ready=owner.GuardDefenseReadiness(domain);}owner.DeliverReady(domain,human(owner.OwnerId),paused,ready);}domain.BindCombatMotionAuthority(()=>owners);}
        internal void Review(PlayableDomain domain,long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,int seed,Func<string,long> human)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Ai);
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
                var view=domain.PlayerSnapshotForArmyAi(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId),!paused&&owner.NeedsArmyRoutes(domain.Tick),owner.ArmyState);
                if(!paused&&!owner.NeedsArmyRoutes(domain.Tick)&&owner.NeedsDefenseRoutes(view))view=domain.PlayerSnapshotForArmyAi(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId),true,owner.ArmyState);
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
            if(paused||stopped||reviewWork!=null||owners.Length==0)return;
            int index=nextReviewOwner;var selected=owners[index];if(!domain.Authorizes(selected.OwnerId))return;
            PlayableSnapshot snapshot;
            using(PerformanceProfile.Measure(ProfileStage.AiProjection))try{
                snapshot=domain.PlayerSnapshotForArmyAi(sequence,status,false,metrics,null,seed,domain.OwnerFor(selected.OwnerId),selected.NeedsArmyRoutes(domain.Tick),selected.ArmyState);
                if(!selected.NeedsArmyRoutes(domain.Tick)&&selected.NeedsDefenseRoutes(snapshot))snapshot=domain.PlayerSnapshotForArmyAi(sequence,status,false,metrics,null,seed,domain.OwnerFor(selected.OwnerId),true,selected.ArmyState);
            }catch(ObservedPathPending){nextReviewOwner=(index+1)%owners.Length;return;}
            var input=selected.CaptureReviewInput(domain.Tick);
            // Only this team's publications can influence this owner's assignment.
            // All views freeze at this phase; worker code sees detached owner copies.
            var team=owners.Where(o=>domain.Authorizes(o.OwnerId)&&snapshot.Participants.Any(p=>p.Id==o.OwnerId&&p.Control==OfflineControl.Ai&&p.Team==snapshot.Team)).ToArray();
            var teamInputs=team.Length>1?team.Select(o=>ReferenceEquals(o,selected)?input:o.CaptureReviewInput(domain.Tick)).ToArray():Array.Empty<PlayableAiOwnerLoop.ReviewCloneInput>();
            var teamViews=teamInputs.Length==0?Array.Empty<PlayableSnapshot>():team.Select(o=>o.OwnerId==selected.OwnerId?snapshot:domain.PlayerSnapshot(sequence,status,false,metrics,null,seed,domain.OwnerFor(o.OwnerId))).ToArray();
            var work=new ReviewWork{Index=index,Human=human(selected.OwnerId),Snapshot=snapshot,Initial=selected.Budget.Capture()};
#if UNITY_INCLUDE_TESTS
            var beforeReview=domain.BeforeOwnerReviewForTests;
#endif
            work.Task=System.Threading.Tasks.Task.Run(()=>{
                var clone=input.Restore();clone.BindActionAllocator(AllocateAction);
                var teamOwners=teamInputs.Select(i=>ReferenceEquals(i,input)?clone:i.Restore()).ToArray();
                System.Collections.Generic.IReadOnlyDictionary<string,AiTeamAssignment> board;
                using(PerformanceProfile.Measure(ProfileStage.TeamBoard))board=AiTeamCoordinator.Resolve(teamOwners.Select((o,i)=>o.PublishTeam(PlayableAiObservation.From(teamViews[i]))),clone.TeamProfile);
                clone.BindTeamAssignment(board.TryGetValue(clone.OwnerId,out var assignment)?assignment:null);
#if UNITY_INCLUDE_TESTS
                beforeReview?.Invoke();
#endif
                clone.Review(snapshot,work.Human);return clone;
            });
            work.Task.ContinueWith(t=>{var observed=t.Exception;},System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            reviewWork=work;nextReviewOwner=(index+1)%owners.Length;
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
        internal void Stop(long tick){stopped=true;InvalidateReview();foreach(var owner in owners)owner.Stop(tick);}
    }
}

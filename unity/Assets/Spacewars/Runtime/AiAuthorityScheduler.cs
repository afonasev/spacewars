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
        internal AiAuthorityScheduler(IEnumerable<PlayableAiOwnerLoop> loops)
        {
            owners=loops.Where(x=>x!=null).OrderBy(x=>x.OwnerId,StringComparer.Ordinal).ToArray();
            if(owners.Select(x=>x.OwnerId).Distinct(StringComparer.Ordinal).Count()!=owners.Length)throw new ArgumentException("Duplicate AI owner.");
            foreach(var owner in owners)owner.BindActionAllocator(()=>checked(++nextActionId));
        }
        internal IReadOnlyList<PlayableAiOwnerLoop> Owners=>Array.AsReadOnly(owners);
        internal void Deliver(PlayableDomain domain,Func<string,long> human,bool paused)
        {foreach(var owner in owners){if(!paused)owner.GuardDefenseDelivery(domain);owner.Deliver(domain,human(owner.OwnerId),paused);}}
        internal void Review(PlayableDomain domain,long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,int seed,Func<string,long> human)
        {
            // Freeze all owner projections/publications at the phase barrier. Review
            // cannot expose an earlier owner's newly committed intentions to a later one.
            var snapshots=new Dictionary<string,PlayableSnapshot>(StringComparer.Ordinal);
            var projectionTimes=new Dictionary<string,long>(StringComparer.Ordinal);
            foreach(var owner in owners.Where(o=>domain.Authorizes(o.OwnerId)))
            {
                long projectionStart=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                var view=domain.PlayerSnapshotForArmyAi(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId),!paused&&owner.NeedsArmyRoutes(domain.Tick),owner.ArmyState);
                if(!paused&&!owner.NeedsArmyRoutes(domain.Tick)&&owner.NeedsDefenseRoutes(view))view=domain.PlayerSnapshotForArmyAi(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId),true,owner.ArmyState);
                snapshots.Add(owner.OwnerId,view);
                if(performanceTrace)projectionTimes.Add(owner.OwnerId,System.Diagnostics.Stopwatch.GetTimestamp()-projectionStart);
            }
            var allied=owners.Where(o=>snapshots.TryGetValue(o.OwnerId,out var view)&&view.Participants.Any(p=>p.Id!=o.OwnerId&&p.Control==OfflineControl.Ai&&p.Team==view.Team&&snapshots.ContainsKey(p.Id))).ToArray();
            var board=AiTeamCoordinator.Resolve(allied.Select(o=>o.PublishTeam(PlayableAiObservation.From(snapshots[o.OwnerId]))),owners.FirstOrDefault()?.TeamProfile??Spacewars.Simulation.Ai.AiProfile.Initial);
            foreach(var owner in owners)owner.BindTeamAssignment(board.TryGetValue(owner.OwnerId,out var assignment)?assignment:null);
            foreach(var owner in owners)
            {
                long begin=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                owner.ReconcileBudget(domain);
                if(!domain.Authorizes(owner.OwnerId)){owner.Stop(domain.Tick);continue;}
                long budget=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                var snapshot=snapshots[owner.OwnerId];
                long projection=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                owner.Review(snapshot,human(owner.OwnerId));
                long review=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                domain.RecordAiState(owner.Checkpoint);
                if(performanceTrace){
                    long end=System.Diagnostics.Stopwatch.GetTimestamp();double scale=1000d/System.Diagnostics.Stopwatch.Frequency;
                    if((end-begin+projectionTimes[owner.OwnerId])*scale>=20)Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "AI_STAGE tick={0} owner={1} budget_ms={2:F4} projection_ms={3:F4} review_ms={4:F4} record_ms={5:F4}",
                        domain.Tick,owner.OwnerId,(budget-begin)*scale,projectionTimes[owner.OwnerId]*scale,(review-projection)*scale,(end-review)*scale));
                }
            }
        }
        internal void PrepareNavigationViews(PlayableDomain domain,int seed,Func<bool> stopping=null)
        {
            foreach(var owner in owners){
                if(stopping?.Invoke()==true)break;
                if(domain.Authorizes(owner.OwnerId))domain.PreparePublicNavigation(domain.OwnerFor(owner.OwnerId),stopping);
            }
        }
        internal void Rebind(PlayableProfile profile){foreach(var owner in owners)owner.Rebind(profile);}
        internal void Stop(long tick){foreach(var owner in owners)owner.Stop(tick);}
    }
}
